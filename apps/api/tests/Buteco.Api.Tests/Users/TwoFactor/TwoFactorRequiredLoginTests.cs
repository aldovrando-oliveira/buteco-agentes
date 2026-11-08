using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// O cadastro obrigatório no login (spec <c>issuer-user-login</c>, "Rota de login abre a sessão do
/// emissor", "Fluxo authorization code com PKCE obrigatório" e "Cadastro da verificação no login,
/// pelo cookie pendente"; design.md da change verificacao-2-etapas-obrigatoria, D1, D2 e D8). Um
/// caso por cenário.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 2.2 e 3.1:
/// <list type="bullet">
/// <item>com o <c>setup</c> aceitando o cookie de quem já tem a verificação,
/// <see cref="TheCookieOfSomeoneWithTheVerification_DoesNotEnroll"/> reprova;</item>
/// <item>com o <c>setup</c> seguinte abrindo um prazo novo, <see cref="TheDeadline_IsTheFirstSetups"/> reprova;</item>
/// <item>com o cookie regravado sem o vencimento, <see cref="TheRewrittenCookie_LastsUntilTheDeadline"/> reprova;</item>
/// <item>com o <c>setup</c> mantendo o segredo anterior, <see cref="SetupAgain_ReplacesTheSecret"/> reprova;</item>
/// <item>com o authorize na regra de antes (<c>!IsTwoFactorEnabled || twoFactorDone</c>),
/// <see cref="TheEnrollmentCookie_IsNoSession_BeforeAndAfterTheSetup"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class TwoFactorRequiredLoginTests(TwoFactorRequiredLoginTests.Fixture factory) : IClassFixture<TwoFactorRequiredLoginTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => IdentityIssuerUserFlow.Client(factory);

    private async Task<(User User, string Pending)> PendingAsync(string prefix, HttpClient client)
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail(prefix), Password);
        var (response, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("cadastro", (await TwoFactorTestClient.JsonAsync(response)).GetProperty("segundoPasso").GetString());
        return (user, pending!);
    }

    private static async Task<(byte[] Secret, string Cookie)> SetupAsync(HttpClient client, string cookie)
    {
        var (response, rewritten) = await TwoFactorTestClient.EnrollmentAsync(client, "setup", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var secret = TwoFactorTestClient.DecodeBase32((await TwoFactorTestClient.JsonAsync(response)).GetProperty("segredo").GetString()!);
        return (secret, rewritten!);
    }

    private Task<(HttpResponseMessage Response, string? Cookie)> ConfirmAsync(HttpClient client, string cookie, string code) =>
        TwoFactorTestClient.EnrollmentAsync(client, "confirm", cookie, new { codigo = code });

    private async Task<User> StoredAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == userId);
    }

    private async Task<int> FailuresAsync(string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles.AsNoTracking()
            .SingleOrDefaultAsync(throttle => throttle.NormalizedEmail == normalizedEmail);
        return row?.ConsecutiveFailures ?? 0;
    }

    private static async Task<HttpResponseMessage> AuthorizeAsync(HttpClient client, string cookie) =>
        await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookie);

    private static void AssertGoesToLogin(HttpResponseMessage authorize)
    {
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/entrar?retorno=", authorize.Headers.Location!.OriginalString);
    }

    private static async Task AssertExpiredAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Buteco.Api.Users.Login.SessionEndpoints.ExpiredMessage, await TwoFactorTestClient.TitleAsync(response));
    }

    [Fact]
    public async Task WithoutTheVerification_TheLoginEnrolls_AndOnlyTheConfirmGivesACode()
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var client = Client();
        var (user, pending) = await PendingAsync("cadastra", client);

        var (setup, enrolling) = await TwoFactorTestClient.EnrollmentAsync(client, "setup", pending);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var body = await TwoFactorTestClient.JsonAsync(setup);
        var segredo = body.GetProperty("segredo").GetString()!;
        Assert.Equal(32, segredo.Length);
        var uri = body.GetProperty("otpauthUri").GetString()!;
        Assert.StartsWith("otpauth://totp/Buteco%20Agentes:", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=Buteco%20Agentes", uri, StringComparison.Ordinal);
        Assert.Contains("algorithm=SHA1&digits=6&period=30", uri, StringComparison.Ordinal);
        var secret = TwoFactorTestClient.DecodeBase32(segredo);

        var (wrong, _) = await ConfirmAsync(client, enrolling!, TwoFactorTestClient.CodeNow(secret, factory.Clock, stepOffset: 5));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("Código inválido.", await TwoFactorTestClient.TitleAsync(wrong));
        Assert.Equal(1, await FailuresAsync(user.NormalizedEmail));
        Assert.False((await StoredAsync(user.Id)).IsTwoFactorEnabled);

        var (right, complete) = await ConfirmAsync(client, enrolling!, TwoFactorTestClient.CodeNow(secret, factory.Clock));
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        var codes = (await TwoFactorTestClient.JsonAsync(right)).GetProperty("codigosRecuperacao").EnumerateArray().Select(code => code.GetString()).ToList();
        Assert.Equal(10, codes.Distinct().Count());
        Assert.NotNull(complete);
        var stored = await StoredAsync(user.Id);
        Assert.True(stored.IsTwoFactorEnabled);
        Assert.Null(stored.TwoFactorPendingSecret);
        Assert.Null(stored.TwoFactorPendingAt);
        Assert.Equal(0, await FailuresAsync(user.NormalizedEmail));
        Assert.NotNull(stored.LastAccessAt);

        var tokens = await TwoFactorTestClient.TokensAsync(client, factory.Services, complete!);
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
    }

    [Fact]
    public async Task ThePasswordWithoutTheVerification_DoesNotRecordTheAccess()
    {
        var (user, _) = await PendingAsync("sem-acesso", Client());

        Assert.Null((await StoredAsync(user.Id)).LastAccessAt);
    }

    [Fact]
    public async Task TheEnrollmentCookie_IsNoSession_BeforeAndAfterTheSetup()
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var client = Client();
        var (_, pending) = await PendingAsync("cookie-de-cadastro", client);

        AssertGoesToLogin(await AuthorizeAsync(client, pending));
        var (_, enrolling) = await SetupAsync(client, pending);
        AssertGoesToLogin(await AuthorizeAsync(client, enrolling));
    }

    [Fact]
    public async Task TheEnrollmentCookie_DoesNotServeTheSecondFactor()
    {
        var client = Client();
        var (_, pending) = await PendingAsync("cadastro-no-segundo-passo", client);
        var (secret, enrolling) = await SetupAsync(client, pending);

        var (response, _) = await TwoFactorTestClient.SecondFactorAsync(client, enrolling, TwoFactorTestClient.CodeNow(secret, factory.Clock));

        await AssertExpiredAsync(response);
    }

    [Fact]
    public async Task TheCookieOfSomeoneWithTheVerification_DoesNotEnroll()
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("ja-tem"), Password);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, user.Id);
        var activeBefore = (await StoredAsync(user.Id)).TwoFactorSecret;
        var client = Client();
        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);

        var (setup, _) = await TwoFactorTestClient.EnrollmentAsync(client, "setup", pending);
        var (confirm, _) = await ConfirmAsync(client, pending!, "123456");

        await AssertExpiredAsync(setup);
        await AssertExpiredAsync(confirm);
        var stored = await StoredAsync(user.Id);
        Assert.Null(stored.TwoFactorPendingSecret);
        Assert.Equal(activeBefore, stored.TwoFactorSecret);
        Assert.Equal(0, await FailuresAsync(user.NormalizedEmail));
    }

    [Fact]
    public async Task WithoutAValidCookie_TheEntryExpired_AndNothingIsCounted()
    {
        var client = Client();

        // Sem cookie.
        await AssertExpiredAsync((await TwoFactorTestClient.EnrollmentAsync(client, "setup", null)).Response);
        await AssertExpiredAsync((await ConfirmAsync(client, null!, "123456")).Response);

        // Vencido: a senha grava 5 minutos.
        var (expired, oldCookie) = await PendingAsync("vencido", client);
        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await AssertExpiredAsync((await TwoFactorTestClient.EnrollmentAsync(client, "setup", oldCookie)).Response);

        // Stamp trocado depois da senha.
        var (stamped, stampedCookie) = await PendingAsync("stamp", client);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE users SET security_stamp = 'outro-stamp' WHERE id = {stamped.Id}");
        }

        await AssertExpiredAsync((await TwoFactorTestClient.EnrollmentAsync(client, "setup", stampedCookie)).Response);

        // Desativado depois da senha.
        var (deactivated, deactivatedCookie) = await PendingAsync("desativado", client);
        await LocalUserSeed.DeactivateAsync(factory.Services, deactivated.Id);
        await AssertExpiredAsync((await TwoFactorTestClient.EnrollmentAsync(client, "setup", deactivatedCookie)).Response);

        foreach (var user in new[] { expired, stamped, deactivated })
        {
            Assert.Null((await StoredAsync(user.Id)).TwoFactorPendingSecret);
            Assert.Equal(0, await FailuresAsync(user.NormalizedEmail));
        }
    }

    [Fact]
    public async Task TheCookieOfOneUser_DoesNotEnrollAnother()
    {
        var client = Client();
        var (a, pendingA) = await PendingAsync("usuario-a", client);
        var (b, pendingB) = await PendingAsync("usuario-b", client);
        var (secretA, _) = await SetupAsync(client, pendingA);
        var (_, enrollingB) = await SetupAsync(client, pendingB);

        var (response, _) = await ConfirmAsync(client, enrollingB, TwoFactorTestClient.CodeNow(secretA, factory.Clock));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False((await StoredAsync(a.Id)).IsTwoFactorEnabled);
        Assert.False((await StoredAsync(b.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task SetupAgain_ReplacesTheSecret()
    {
        var client = Client();
        var (_, pending) = await PendingAsync("setup-de-novo", client);
        var (first, enrolling) = await SetupAsync(client, pending);
        var (second, enrollingAgain) = await SetupAsync(client, enrolling);

        var (old, _) = await ConfirmAsync(client, enrollingAgain, TwoFactorTestClient.CodeNow(first, factory.Clock));
        var (current, _) = await ConfirmAsync(client, enrollingAgain, TwoFactorTestClient.CodeNow(second, factory.Clock));

        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task TheDeadline_IsTheFirstSetups()
    {
        var client = Client();
        var (user, pending) = await PendingAsync("prazo", client);
        var (_, enrolling) = await SetupAsync(client, pending);
        factory.Clock.Advance(TimeSpan.FromMinutes(8));
        var (secret, enrollingAgain) = await SetupAsync(client, enrolling);
        factory.Clock.Advance(TimeSpan.FromMinutes(2.5));

        var (response, _) = await ConfirmAsync(client, enrollingAgain, TwoFactorTestClient.CodeNow(secret, factory.Clock));

        await AssertExpiredAsync(response);
        Assert.False((await StoredAsync(user.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task TheRewrittenCookie_LastsUntilTheDeadline()
    {
        var client = Client();
        var (user, pending) = await PendingAsync("cookie-regravado", client);
        factory.Clock.Advance(TimeSpan.FromMinutes(4));
        var (secret, enrolling) = await SetupAsync(client, pending);
        factory.Clock.Advance(TimeSpan.FromMinutes(9));

        var (response, _) = await ConfirmAsync(client, enrolling, TwoFactorTestClient.CodeNow(secret, factory.Clock));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await StoredAsync(user.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task ConfirmWithoutASetupOnThisCookie_TheEntryExpired()
    {
        var client = Client();
        var (user, pending) = await PendingAsync("sem-setup-neste-cookie", client);
        var (secret, _) = await SetupAsync(client, pending);
        var (_, newPending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);

        var (response, _) = await ConfirmAsync(client, newPending!, TwoFactorTestClient.CodeNow(secret, factory.Clock));

        await AssertExpiredAsync(response);
        Assert.False((await StoredAsync(user.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task WithTheEmailLocked_TheEnrollmentRoutesAre423_WithoutEffect()
    {
        var client = Client();
        var (user, pending) = await PendingAsync("bloqueado", client);
        var (secret, enrolling) = await SetupAsync(client, pending);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LocalUserSeed.PostSessionAsync(client, user.Email, "senha-errada");
        }

        var (setup, _) = await TwoFactorTestClient.EnrollmentAsync(client, "setup", enrolling);
        var (confirm, _) = await ConfirmAsync(client, enrolling, TwoFactorTestClient.CodeNow(secret, factory.Clock));

        Assert.Equal((HttpStatusCode)423, setup.StatusCode);
        Assert.True(setup.Headers.Contains("Retry-After"));
        Assert.Equal((HttpStatusCode)423, confirm.StatusCode);
        Assert.False((await StoredAsync(user.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task AnotherOrigin_Is403_WithoutEffect()
    {
        var client = Client();
        var (user, pending) = await PendingAsync("outra-origem", client);

        var (setup, _) = await TwoFactorTestClient.EnrollmentAsync(client, "setup", pending, origin: "https://outro.exemplo");
        var (confirm, _) = await TwoFactorTestClient.EnrollmentAsync(client, "confirm", pending, new { codigo = "123456" }, origin: "https://outro.exemplo");

        Assert.Equal(HttpStatusCode.Forbidden, setup.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
        Assert.Null((await StoredAsync(user.Id)).TwoFactorPendingSecret);
    }

    [Fact]
    public async Task ContinueAfterFiveMinutes_TheAuthorizeSendsToLogin_AndTheNewAppAndCodesWork()
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var client = Client();
        var (user, pending) = await PendingAsync("continuar-depois", client);
        var (secret, enrolling) = await SetupAsync(client, pending);
        var (confirm, complete) = await ConfirmAsync(client, enrolling, TwoFactorTestClient.CodeNow(secret, factory.Clock));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var codes = (await TwoFactorTestClient.JsonAsync(confirm)).GetProperty("codigosRecuperacao").EnumerateArray().Select(code => code.GetString()!).ToList();

        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        AssertGoesToLogin(await AuthorizeAsync(client, complete!));

        var withApp = await TwoFactorTestClient.CompleteSessionCookieAsync(client, user.Email, Password, TwoFactorTestClient.CodeNow(secret, factory.Clock));
        Assert.False(string.IsNullOrEmpty(IdentityIssuerUserFlow.CodeFrom(await AuthorizeAsync(client, withApp))));
        await TwoFactorTestClient.CompleteSessionCookieAsync(client, user.Email, Password, codes[0]);
    }
}
