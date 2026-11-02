using System.Net;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;

namespace Buteco.Api.Tests.Users.Account;

/// <summary>
/// "Lista das sessões vivas, com a sessão atual marcada", "Encerrar uma sessão própria" e
/// "Encerrar todas as outras sessões próprias" (spec <c>user-account</c>; design.md da change
/// rotas-conta-usuarios, D4).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 5.5:
/// <list type="bullet">
/// <item><c>EXISTS</c> do refresh vivo retirado → <see cref="AnAbandonedLogin_IsNotASession"/> reprova;</item>
/// <item>conferência do <c>subject</c> retirada do encerrar uma → <see cref="SomeoneElsesSession_Is404_AndStaysAlive"/> reprova;</item>
/// <item><c>id &lt;&gt; @atual</c> retirado → <see cref="EndTheOthers_KeepsOnlyTheCurrentOne"/> reprova;</item>
/// <item><c>oi_au_id</c> fora do principal → <see cref="TwoLogins_TwoSessions_OnlyTheTokensOneIsCurrent"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class AccountSessionsTests(AccountSessionsTests.Fixture factory) : IClassFixture<AccountSessionsTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private async Task<List<JsonElement>> ListAsync(SignedInUser me)
    {
        var response = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Get, "/account/sessions", me.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await TwoFactorTestClient.JsonAsync(response)).EnumerateArray().ToList();
    }

    private Task<HttpResponseMessage> EndAsync(SignedInUser me, string id) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, $"/account/sessions/{id}/revoke", me.AccessToken);

    private Task<HttpResponseMessage> EndOthersAsync(SignedInUser me) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, "/account/sessions/revoke-others", me.AccessToken);

    [Fact]
    public async Task TwoLogins_TwoSessions_OnlyTheTokensOneIsCurrent()
    {
        var first = await SignedInUser.CreateAsync(factory, "duas");
        var second = await first.SignInAgainAsync(factory);

        var sessions = await ListAsync(second);

        Assert.Equal(2, sessions.Count);
        Assert.Equal([second.AuthorizationId], sessions.Where(s => s.GetProperty("atual").GetBoolean()).Select(s => s.GetProperty("id").GetString()));
        Assert.Contains(sessions, s => s.GetProperty("id").GetString() == first.AuthorizationId && !s.GetProperty("atual").GetBoolean());
    }

    [Fact]
    public async Task TheLastUse_FollowsTheRenewal()
    {
        var me = await SignedInUser.CreateAsync(factory, "ultimo-uso");
        var before = Assert.Single(await ListAsync(me));

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var renewed = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(SignedInUser.Anonymous(factory), me.RefreshToken));
        var after = Assert.Single(await ListAsync(me with { Tokens = renewed }));

        Assert.Equal(before.GetProperty("criadaEm").GetDateTimeOffset(), after.GetProperty("criadaEm").GetDateTimeOffset());
        // O último uso vem do iat do token, truncado em segundos: até 1 s abaixo dos 5 min.
        Assert.True(after.GetProperty("ultimoUso").GetDateTimeOffset() >= before.GetProperty("ultimoUso").GetDateTimeOffset() + TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task TheLastUse_IsNeverBeforeTheCreation()
    {
        var me = await SignedInUser.CreateAsync(factory, "piso");

        var session = Assert.Single(await ListAsync(me));

        // A data do token sai do iat, em segundos; a da autorização tem a fração.
        Assert.True(session.GetProperty("ultimoUso").GetDateTimeOffset() >= session.GetProperty("criadaEm").GetDateTimeOffset());
    }

    [Fact]
    public async Task AnAbandonedLogin_IsNotASession()
    {
        var me = await SignedInUser.CreateAsync(factory, "abandonado");
        var client = SignedInUser.Anonymous(factory);
        var cookie = await IdentityIssuerUserFlow.SessionCookieAsync(client, me.User.Email, me.Password);
        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookie);
        IdentityIssuerUserFlow.CodeFrom(authorize);
        Assert.Equal(2, (await UserSessionsSeed.AuthorizationStatusesAsync(factory.Services, me.User.Id)).Count(status => status == "valid"));

        var sessions = await ListAsync(me);

        Assert.Equal([me.AuthorizationId], sessions.Select(s => s.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task ASessionPastItsRefresh_IsNotASession_BeforeThePrune()
    {
        var me = await SignedInUser.CreateAsync(factory, "vencida");
        var stale = await me.SignInAgainAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var past = factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
            await db.Set<OpenIddictEntityFrameworkCoreToken>()
                .Where(token => token.Authorization!.Id == stale.AuthorizationId)
                .ExecuteUpdateAsync(set => set.SetProperty(token => token.ExpirationDate, past));
        }

        var sessions = await ListAsync(me);

        Assert.Equal([me.AuthorizationId], sessions.Select(s => s.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task EndOne_DisappearsAndStopsRenewing_TheCurrentKeepsRenewing()
    {
        var me = await SignedInUser.CreateAsync(factory, "encerrar-uma");
        var other = await me.SignInAgainAsync(factory);

        Assert.Equal(HttpStatusCode.NoContent, (await EndAsync(me, other.AuthorizationId)).StatusCode);

        Assert.Equal([me.AuthorizationId], (await ListAsync(me)).Select(s => s.GetProperty("id").GetString()));
        Assert.False(await SignedInUser.RenewsAsync(factory, other.RefreshToken));
        Assert.True(await SignedInUser.RenewsAsync(factory, me.RefreshToken));
    }

    [Fact]
    public async Task SomeoneElsesSession_Is404_AndStaysAlive()
    {
        var me = await SignedInUser.CreateAsync(factory, "minha");
        var someone = await SignedInUser.CreateAsync(factory, "de-outro");

        Assert.Equal(HttpStatusCode.NotFound, (await EndAsync(me, someone.AuthorizationId)).StatusCode);

        Assert.True(await SignedInUser.RenewsAsync(factory, someone.RefreshToken));
    }

    [Fact]
    public async Task EndTheCurrentOne_IsAllowed_AndItStopsRenewing()
    {
        var me = await SignedInUser.CreateAsync(factory, "a-atual");

        Assert.Equal(HttpStatusCode.NoContent, (await EndAsync(me, me.AuthorizationId)).StatusCode);

        Assert.False(await SignedInUser.RenewsAsync(factory, me.RefreshToken));
    }

    [Fact]
    public async Task EndTheOthers_KeepsOnlyTheCurrentOne()
    {
        var first = await SignedInUser.CreateAsync(factory, "outras");
        var second = await first.SignInAgainAsync(factory);
        var third = await first.SignInAgainAsync(factory);

        var response = await EndOthersAsync(third);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await TwoFactorTestClient.JsonAsync(response)).GetProperty("encerradas").GetInt32());
        Assert.Equal([third.AuthorizationId], (await ListAsync(third)).Select(s => s.GetProperty("id").GetString()));
        Assert.False(await SignedInUser.RenewsAsync(factory, first.RefreshToken));
        Assert.False(await SignedInUser.RenewsAsync(factory, second.RefreshToken));
        Assert.True(await SignedInUser.RenewsAsync(factory, third.RefreshToken));
    }

    [Fact]
    public async Task EndTheOthers_LeavesSomeoneElsesSessionsAlone()
    {
        var me = await SignedInUser.CreateAsync(factory, "outras-minhas");
        var someone = await SignedInUser.CreateAsync(factory, "outras-de-outro");

        Assert.Equal(HttpStatusCode.OK, (await EndOthersAsync(me)).StatusCode);

        Assert.True(await SignedInUser.RenewsAsync(factory, someone.RefreshToken));
    }
}
