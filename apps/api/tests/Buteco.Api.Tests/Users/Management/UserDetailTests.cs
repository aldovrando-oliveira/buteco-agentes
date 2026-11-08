using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users.Management;

/// <summary>
/// "Detalhe de usuário com a contagem de sessões" (spec <c>user-management</c>; design.md da
/// change rotas-conta-usuarios, D4 e D6), e o cenário "A redefinição encerra as sessões na
/// lista" de <c>two-factor-totp</c>, pela rota.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 6.3: com a contagem sem o
/// <c>EXISTS</c> do refresh vivo, <see cref="AnAbandonedLogin_DoesNotCount"/> reprova.
/// </para>
/// </summary>
public class UserDetailTests(UserDetailTests.Fixture factory) : IClassFixture<UserDetailTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private Task<HttpResponseMessage> GetAsync(SignedInUser caller, Guid id) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Get, $"/users/{id}", caller.AccessToken);

    [Fact]
    public async Task CountsTheLiveSessions_AndTheLastActivity()
    {
        var caller = await SignedInUser.CreateAsync(factory, "detalhe-quem-chama");
        var target = await SignedInUser.CreateAsync(factory, "detalhe-alvo");
        await target.SignInAgainAsync(factory);
        factory.Clock.Advance(TimeSpan.FromMinutes(3));
        await SignedInUser.RenewsAsync(factory, target.RefreshToken);
        var renewedAt = factory.Clock.GetUtcNow();

        var response = await GetAsync(caller, target.User.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await TwoFactorTestClient.JsonAsync(response);
        var sessions = body.GetProperty("sessoes");
        Assert.Equal(2, sessions.GetProperty("ativas").GetInt32());
        Assert.True(Math.Abs((sessions.GetProperty("ultimaAtividade").GetDateTimeOffset() - renewedAt).TotalSeconds) < 2);
        Assert.Equal(target.User.Email, body.GetProperty("email").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("desativadoEm").ValueKind);
    }

    [Fact]
    public async Task WithoutSessions_ZeroAndNull()
    {
        var caller = await SignedInUser.CreateAsync(factory, "detalhe-sem");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("sem-sessao"), SignedInUser.DefaultPassword);

        var sessions = (await TwoFactorTestClient.JsonAsync(await GetAsync(caller, target.Id))).GetProperty("sessoes");

        Assert.Equal(0, sessions.GetProperty("ativas").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, sessions.GetProperty("ultimaAtividade").ValueKind);
    }

    [Fact]
    public async Task AnAbandonedLogin_DoesNotCount()
    {
        var caller = await SignedInUser.CreateAsync(factory, "detalhe-abandono");
        var target = await SignedInUser.CreateAsync(factory, "abandonou");
        var client = SignedInUser.Anonymous(factory);
        var cookie = await IdentityIssuerUserFlow.SessionCookieAsync(client, factory.Services, target.User.Email, target.Password);
        IdentityIssuerUserFlow.CodeFrom(await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookie));

        var sessions = (await TwoFactorTestClient.JsonAsync(await GetAsync(caller, target.User.Id))).GetProperty("sessoes");

        Assert.Equal(1, sessions.GetProperty("ativas").GetInt32());
    }

    [Fact]
    public async Task TheTwoFactorReset_EndsTheSessionsShownInTheDetail()
    {
        var caller = await SignedInUser.CreateAsync(factory, "detalhe-redefine");
        var target = await SignedInUser.CreateAsync(factory, "redefinido");
        await target.SignInAgainAsync(factory);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, target.User.Id);

        var reset = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, $"/users/{target.User.Id}/two-factor/reset", caller.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(0, (await TwoFactorTestClient.JsonAsync(await GetAsync(caller, target.User.Id))).GetProperty("sessoes").GetProperty("ativas").GetInt32());
    }

    [Fact]
    public async Task UnknownUser_Is404()
    {
        var caller = await SignedInUser.CreateAsync(factory, "detalhe-404");

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(caller, Guid.NewGuid())).StatusCode);
    }
}
