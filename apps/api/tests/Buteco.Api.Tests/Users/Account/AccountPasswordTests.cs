using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.Account;

/// <summary>
/// "Trocar a própria senha, com a senha atual", "Política de senha" e "Toda troca do carimbo de
/// segurança revoga as sessões da pessoa" (spec <c>user-account</c>; design.md da change
/// rotas-conta-usuarios, D2 e D3).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 5.4:
/// <list type="bullet">
/// <item>política aplicada também à senha atual → <see cref="ACurrentPasswordShorterThanThePolicy_StillWorks"/> reprova;</item>
/// <item>troca do stamp retirada → <see cref="TheChange_RefusesASessionCookieInFlight"/> reprova. A renovação não serve de guarda do stamp: a revogação já a derruba sozinha;</item>
/// <item>revogação retirada → <see cref="TheChange_EndsEverySession_IncludingTheCurrentOne"/> reprova (a lista mostra as sessões);</item>
/// <item><c>RecordFailureAsync</c> retirado → <see cref="FiveWrongCurrentPasswords_LockTheEmail_AndNothingIsChecked"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class AccountPasswordTests(AccountPasswordTests.Fixture factory) : IClassFixture<AccountPasswordTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string NewPassword = "uma-senha-nova-longa";

    private Task<HttpResponseMessage> ChangeAsync(SignedInUser me, string? current, string? next) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, "/account/password", me.AccessToken,
            new { senhaAtual = current, novaSenha = next });

    private Task<HttpResponseMessage> LoginAsync(SignedInUser me, string password) =>
        LocalUserSeed.PostSessionAsync(SignedInUser.Anonymous(factory), me.User.Email, password);

    [Fact]
    public async Task WithoutTheCurrentPassword_Is401()
    {
        var me = await SignedInUser.CreateAsync(factory, "sem-atual");

        Assert.Equal(HttpStatusCode.Unauthorized, (await ChangeAsync(me, null, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ChangeAsync(me, "senha-errada-do-teste", NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(me, me.Password)).StatusCode);
    }

    [Fact]
    public async Task FiveWrongCurrentPasswords_LockTheEmail_AndNothingIsChecked()
    {
        var me = await SignedInUser.CreateAsync(factory, "bloqueio");
        var hashBefore = (await SignedInUser.ReloadAsync(factory.Services, me.User.Id)).PasswordHash;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await ChangeAsync(me, "senha-errada-do-teste", NewPassword)).StatusCode);
        }

        var locked = await ChangeAsync(me, me.Password, NewPassword);

        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        Assert.NotNull(locked.Headers.RetryAfter);
        Assert.Equal(hashBefore, (await SignedInUser.ReloadAsync(factory.Services, me.User.Id)).PasswordHash);
    }

    [Fact]
    public async Task ANewPasswordOutsideThePolicy_Is400_AndTheCurrentOneKeepsWorking()
    {
        var me = await SignedInUser.CreateAsync(factory, "politica");

        Assert.Equal(HttpStatusCode.BadRequest, (await ChangeAsync(me, me.Password, "curta-11-ch")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ChangeAsync(me, me.Password, new string('a', 129))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(me, me.Password)).StatusCode);
    }

    [Fact]
    public async Task ACurrentPasswordShorterThanThePolicy_StillWorks()
    {
        var me = await SignedInUser.CreateAsync(factory, "senha-curta", password: "curta-08");

        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(me, "curta-08")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ChangeAsync(me, "curta-08", new string('b', 12))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(me, new string('b', 12))).StatusCode);
    }

    [Fact]
    public async Task TheChange_ReplacesThePassword()
    {
        var me = await SignedInUser.CreateAsync(factory, "troca");

        Assert.Equal(HttpStatusCode.NoContent, (await ChangeAsync(me, me.Password, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(me, me.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(me, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task TheChange_EndsEverySession_IncludingTheCurrentOne()
    {
        var me = await SignedInUser.CreateAsync(factory, "sessoes");
        var other = await me.SignInAgainAsync(factory);

        Assert.Equal(HttpStatusCode.NoContent, (await ChangeAsync(me, me.Password, NewPassword)).StatusCode);

        Assert.False(await SignedInUser.RenewsAsync(factory, me.RefreshToken));
        Assert.False(await SignedInUser.RenewsAsync(factory, other.RefreshToken));
        var fresh = await SignedInUser.SignInAsync(factory, me.User, NewPassword);
        await using var scope = factory.Services.CreateAsyncScope();
        var live = await scope.ServiceProvider.GetRequiredService<UserSessions>().ListLiveAsync(me.User.Id, CancellationToken.None);
        Assert.Equal([fresh.AuthorizationId], live.Select(session => session.Id));
    }

    [Fact]
    public async Task TheChange_RefusesASessionCookieInFlight()
    {
        var me = await SignedInUser.CreateAsync(factory, "cookie-em-voo");
        var client = SignedInUser.Anonymous(factory);
        var cookie = await IdentityIssuerUserFlow.SessionCookieAsync(client, me.User.Email, me.Password);

        Assert.Equal(HttpStatusCode.NoContent, (await ChangeAsync(me, me.Password, NewPassword)).StatusCode);

        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookie);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/entrar", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSuccess_DoesNotResetTheLockCount()
    {
        var me = await SignedInUser.CreateAsync(factory, "nao-zera");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await ChangeAsync(me, "senha-errada-do-teste", NewPassword)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ChangeAsync(me, me.Password, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(me, "senha-errada-do-teste")).StatusCode);

        Assert.Equal((HttpStatusCode)423, (await LoginAsync(me, NewPassword)).StatusCode);
    }
}
