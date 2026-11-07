using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// O segundo passo do login (spec <c>issuer-user-login</c>, "Rota de login abre a sessão do
/// emissor", "Fluxo authorization code com PKCE obrigatório" e "O segundo passo completa a
/// sessão do emissor"; spec <c>two-factor-totp</c>, "Janela de um passo e recusa do código já
/// usado"; design.md da change verificacao-2-etapas-totp, D2, D3 e D6). Um caso por cenário.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 7.3:
/// <list type="bullet">
/// <item>com o authorize sem conferir o claim da sessão completa,
/// <see cref="WithTheVerification_ThePasswordOpensASecondStep_AndOnlyTheCodeGivesACode"/> reprova;</item>
/// <item>com a regra pelo cookie, e não pelo estado do usuário,
/// <see cref="CookieFromBeforeTurningTheVerificationOn_IsNoSession"/> reprova;</item>
/// <item>com o uso do código de recuperação sem <c>used_at IS NULL</c>,
/// <see cref="RecoveryCode_CompletesTheSession_ButNotTwice"/> reprova;</item>
/// <item>com a conferência sem o último passo, <see cref="SameCodeTwice_TheSecondIsRefused"/> e
/// <see cref="PreviousStep_AfterALaterOneWasAccepted_IsRefused"/> reprovam.</item>
/// </list>
/// </para>
/// </summary>
public class TwoFactorLoginTests(TwoFactorLoginTests.Fixture factory) : IClassFixture<TwoFactorLoginTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => IdentityIssuerUserFlow.Client(factory);

    private async Task<(User User, TwoFactorTestClient.Enabled TwoFactor)> UserWithTheVerificationAsync(string prefix)
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail(prefix), Password);
        var enabled = await TwoFactorTestClient.SeedEnabledAsync(factory.Services, user.Id);
        TwoFactorTestClient.NextStep(factory.Clock);
        return (user, enabled);
    }

    private async Task<int> FailuresAsync(string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles.AsNoTracking()
            .SingleOrDefaultAsync(throttle => throttle.NormalizedEmail == normalizedEmail);
        return row?.ConsecutiveFailures ?? 0;
    }

    [Fact]
    public async Task WithTheVerification_ThePasswordOpensASecondStep_AndOnlyTheCodeGivesACode()
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var (user, twoFactor) = await UserWithTheVerificationAsync("dois-passos");
        var client = Client();

        var (password, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
        Assert.Equal("totp", (await TwoFactorTestClient.JsonAsync(password)).GetProperty("segundoPasso").GetString());
        Assert.NotNull(pending);

        var withPending = await IdentityIssuerUserFlow.AuthorizeAsync(
            client, IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), pending);
        Assert.Equal(HttpStatusCode.Redirect, withPending.StatusCode);
        Assert.StartsWith("/entrar?retorno=", withPending.Headers.Location!.OriginalString);

        var (code, complete) = await TwoFactorTestClient.SecondFactorAsync(client, pending, TwoFactorTestClient.CodeNow(twoFactor.Secret, factory.Clock));
        Assert.Equal(HttpStatusCode.NoContent, code.StatusCode);
        Assert.NotNull(complete);

        var tokens = await TwoFactorTestClient.TokensAsync(client, factory.Services, complete);
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
    }

    [Fact]
    public async Task WithoutTheVerification_TheLoginIsStill204()
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("sem-dois"), Password);

        var (response, cookie) = await TwoFactorTestClient.PasswordStepAsync(Client(), user.Email, Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(cookie);
    }

    [Fact]
    public async Task CookieFromBeforeTurningTheVerificationOn_IsNoSession()
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("antes"), Password);
        var client = Client();
        var cookie = await IdentityIssuerUserFlow.SessionCookieAsync(client, user.Email, Password);

        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, user.Id);
        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(
            client, IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookie);

        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/entrar?retorno=", authorize.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task RecoveryCode_CompletesTheSession_ButNotTwice()
    {
        var (user, twoFactor) = await UserWithTheVerificationAsync("recuperacao");
        var client = Client();
        var code = twoFactor.RecoveryCodes[0];

        var (_, first) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        var (accepted, _) = await TwoFactorTestClient.SecondFactorAsync(client, first, code);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        var (_, second) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        var (again, _) = await TwoFactorTestClient.SecondFactorAsync(client, second, code);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Equal("Código inválido.", await TwoFactorTestClient.TitleAsync(again));
    }

    [Fact]
    public async Task SameCodeTwice_TheSecondIsRefused()
    {
        var (user, twoFactor) = await UserWithTheVerificationAsync("mesmo-codigo");
        var client = Client();
        var code = TwoFactorTestClient.CodeNow(twoFactor.Secret, factory.Clock);

        var (_, first) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.NoContent, (await TwoFactorTestClient.SecondFactorAsync(client, first, code)).Response.StatusCode);

        var (_, second) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TwoFactorTestClient.SecondFactorAsync(client, second, code)).Response.StatusCode);
    }

    [Fact]
    public async Task PreviousStep_AfterALaterOneWasAccepted_IsRefused()
    {
        var (user, twoFactor) = await UserWithTheVerificationAsync("passo-anterior");
        var client = Client();

        var (_, first) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        var later = TwoFactorTestClient.CodeNow(twoFactor.Secret, factory.Clock, stepOffset: 1);
        Assert.Equal(HttpStatusCode.NoContent, (await TwoFactorTestClient.SecondFactorAsync(client, first, later)).Response.StatusCode);

        var (_, second) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        var current = TwoFactorTestClient.CodeNow(twoFactor.Secret, factory.Clock);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TwoFactorTestClient.SecondFactorAsync(client, second, current)).Response.StatusCode);
    }

    [Fact]
    public async Task SimultaneousSubmissionsOfTheSameCode_OnlyOneIsAccepted()
    {
        var (user, twoFactor) = await UserWithTheVerificationAsync("simultaneo");
        var client = Client();
        var (_, firstCookie) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        var (_, secondCookie) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        var code = TwoFactorTestClient.CodeNow(twoFactor.Secret, factory.Clock);

        var responses = await Task.WhenAll(
            TwoFactorTestClient.SecondFactorAsync(client, firstCookie, code),
            TwoFactorTestClient.SecondFactorAsync(client, secondCookie, code));

        Assert.Single(responses, response => response.Response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, response => response.Response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithoutAPendingSession_TheEntryExpired_AndNothingIsCounted()
    {
        var (user, twoFactor) = await UserWithTheVerificationAsync("sem-pendente");
        var client = Client();
        var code = TwoFactorTestClient.CodeNow(twoFactor.Secret, factory.Clock);

        var (withoutCookie, _) = await TwoFactorTestClient.SecondFactorAsync(client, cookie: null, code);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutCookie.StatusCode);
        Assert.Equal("A entrada expirou. Informe e-mail e senha de novo.", await TwoFactorTestClient.TitleAsync(withoutCookie));

        // Redefinida no meio: o stamp do cookie deixa de ser o gravado.
        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.FindAsync(user.Id))!.ResetTwoFactor();
            await db.SaveChangesAsync();
        }

        var (afterReset, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, code);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReset.StatusCode);
        Assert.Equal("A entrada expirou. Informe e-mail e senha de novo.", await TwoFactorTestClient.TitleAsync(afterReset));
        Assert.Equal(0, await FailuresAsync(user.NormalizedEmail));
    }

    // O painel reconhece este texto por igualdade (apps/frontend/src/features/auth/messages.ts,
    // ENTRADA_EXPIROU): com ele, o /entrar volta a e-mail e senha; com outro, mostra
    // "Código inválido." no passo do código. O literal fica aqui, e não a constante, para
    // que mudar o texto no servidor reprove este teste. A troca por um campo de tipo é a #247.
    [Fact]
    public async Task SecondFactor_ExpiredPending_KeepsTheMessageThePanelRecognizes()
    {
        var (user, twoFactor) = await UserWithTheVerificationAsync("vencido");
        var client = Client();
        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);

        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        var (response, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, TwoFactorTestClient.CodeNow(twoFactor.Secret, factory.Clock));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("A entrada expirou. Informe e-mail e senha de novo.", await TwoFactorTestClient.TitleAsync(response));
    }

    [Fact]
    public async Task OriginFromAnotherSite_IsRefusedWithoutCheckingTheCode()
    {
        var (user, twoFactor) = await UserWithTheVerificationAsync("origem");
        var client = Client();
        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        var code = TwoFactorTestClient.CodeNow(twoFactor.Secret, factory.Clock);

        var (refused, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, code, origin: "https://outro.exemplo");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // O código não foi marcado: ainda serve.
        Assert.Equal(HttpStatusCode.NoContent, (await TwoFactorTestClient.SecondFactorAsync(client, pending, code)).Response.StatusCode);
    }
}
