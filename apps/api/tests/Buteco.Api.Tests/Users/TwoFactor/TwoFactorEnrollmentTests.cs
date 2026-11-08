using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// A verificação da própria pessoa em <c>/account/two-factor</c>: a situação e a regeneração dos
/// códigos (spec <c>two-factor-totp</c>, "Situação da verificação", "Códigos de recuperação" e
/// "Regeneração dos códigos de recuperação"; design.md da change verificacao-2-etapas-totp, D6 e
/// D8). Desde a change verificacao-2-etapas-obrigatoria (D5, Q5), o cadastro é só o do login, em
/// <see cref="TwoFactorRequiredLoginTests"/>, e não há cadastro nem remoção por access token: os
/// casos deles saíram com os requisitos, e os dois últimos daqui fixam que as rotas não existem.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md da #218, 9.2: com a
/// regeneração acrescentando sem apagar, <see cref="Regenerate_InvalidatesTheOldCodes"/> reprova.
/// </para>
/// </summary>
public class TwoFactorEnrollmentTests(TwoFactorEnrollmentTests.Fixture factory) : IClassFixture<TwoFactorEnrollmentTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private HttpClient Client() => TwoFactorAccount.Anonymous(factory);

    private async Task<Buteco.Api.Users.User> StoredAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == userId);
    }

    [Fact]
    public async Task TheSecret_NeverComesBackInARead()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "leitura");
        var secret = account.Secret;

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Get, "/account/two-factor", account.AccessToken);

        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Buteco.Api.Users.TwoFactor.Base32.Encode(secret), text, StringComparison.Ordinal);
        Assert.DoesNotContain("otpauth", text, StringComparison.Ordinal);
        var body = await TwoFactorTestClient.JsonAsync(response);
        Assert.True(body.GetProperty("ativa").GetBoolean());
        Assert.Equal(10, body.GetProperty("codigosRestantes").GetInt32());
        Assert.False(body.TryGetProperty("cadastroDisponivel", out _));
    }

    [Fact]
    public async Task TheConfirmationCode_DoesNotServeToLogIn()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "codigo-da-confirmacao");
        var secret = account.Secret;
        var client = Client();

        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, account.User.Email, TwoFactorAccount.Password);
        var (response, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, TwoFactorTestClient.CodeNow(secret, factory.Clock));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AfterUsingARecoveryCode_NineRemain()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "nove");
        var codes = account.RecoveryCodes;
        var client = Client();
        await TwoFactorTestClient.CompleteSessionCookieAsync(client, account.User.Email, TwoFactorAccount.Password, codes[3]);

        var body = await TwoFactorTestClient.JsonAsync(
            await TwoFactorTestClient.SendAsync(client, HttpMethod.Get, "/account/two-factor", account.AccessToken));

        Assert.True(body.GetProperty("ativa").GetBoolean());
        Assert.Equal(9, body.GetProperty("codigosRestantes").GetInt32());
    }

    [Fact]
    public async Task Regenerate_InvalidatesTheOldCodes()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "regenera");
        var (secret, oldCodes) = (account.Secret, account.RecoveryCodes);
        TwoFactorTestClient.NextStep(factory.Clock);
        var client = Client();

        var response = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/recovery-codes", account.AccessToken,
            new { codigo = TwoFactorTestClient.CodeNow(secret, factory.Clock) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newCodes = (await TwoFactorTestClient.JsonAsync(response)).GetProperty("codigosRecuperacao").EnumerateArray().Select(code => code.GetString()).ToList();
        Assert.Equal(10, newCodes.Count);
        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, account.User.Email, TwoFactorAccount.Password);
        var (old, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, oldCodes[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
    }

    [Fact]
    public async Task Regenerate_WithoutTheVerification_Is409()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "sem-verificacao");
        // O access token de uma sessão de antes da obrigatoriedade, de quem não tem a verificação.
        await TwoFactorTestClient.SeedDisabledAsync(factory.Services, account.User.Id);

        Assert.Equal(HttpStatusCode.Conflict,
            (await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, "/account/two-factor/recovery-codes", account.AccessToken, new { codigo = "123456" })).StatusCode);
    }

    [Fact]
    public async Task ThereIsNoRemoveRoute()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "sem-remover");

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, "/account/two-factor/remove", account.AccessToken,
            new { codigo = account.RecoveryCodes[0] });

        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True((await StoredAsync(account.User.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task ThereIsNoEnrollmentByAccessToken()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "sem-cadastro-por-token");
        // De quem não tem a verificação: se a rota existisse, este seria o caso que cadastraria.
        await TwoFactorTestClient.SeedDisabledAsync(factory.Services, account.User.Id);
        var client = Client();

        var setup = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/setup", account.AccessToken, new { senha = TwoFactorAccount.Password });
        var confirm = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/confirm", account.AccessToken, new { codigo = "123456" });

        Assert.NotEqual(HttpStatusCode.OK, setup.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, confirm.StatusCode);
        var stored = await StoredAsync(account.User.Id);
        Assert.Null(stored.TwoFactorPendingSecret);
        Assert.False(stored.IsTwoFactorEnabled);
    }
}
