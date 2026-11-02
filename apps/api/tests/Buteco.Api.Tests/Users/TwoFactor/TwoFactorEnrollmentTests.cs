using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// O cadastro, a situação, a regeneração e a remoção (spec <c>two-factor-totp</c>, "Cadastro
/// em dois tempos", "Situação da verificação", "Códigos de recuperação" e "Regeneração dos
/// códigos e remoção"; design.md da change verificacao-2-etapas-totp, D6 e D8). Um caso por
/// cenário. O cadastro fechado fica em <see cref="TwoFactorEnrollmentClosedTests"/>.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 8.2 e 9.2:
/// <list type="bullet">
/// <item>com o <c>confirm</c> ligando sem conferir o código,
/// <see cref="Confirm_WithAWrongCode_DoesNotTurnItOn"/> reprova;</item>
/// <item>com o pendente sem prazo, <see cref="Confirm_AfterTenMinutes_Is400"/> reprova;</item>
/// <item>com a regeneração acrescentando sem apagar,
/// <see cref="Regenerate_InvalidatesTheOldCodes"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class TwoFactorEnrollmentTests(TwoFactorEnrollmentTests.Fixture factory) : IClassFixture<TwoFactorEnrollmentTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private HttpClient Client() => TwoFactorAccount.Anonymous(factory);

    private async Task<int> FailuresAsync(string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles.AsNoTracking()
            .SingleOrDefaultAsync(throttle => throttle.NormalizedEmail == normalizedEmail);
        return row?.ConsecutiveFailures ?? 0;
    }

    private async Task<Buteco.Api.Users.User> StoredAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == userId);
    }

    [Fact]
    public async Task Setup_WithTheWrongPassword_Is401_AndCounts()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "setup-senha");

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, "/account/two-factor/setup", account.AccessToken, new { senha = "outra" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null((await StoredAsync(account.User.Id)).TwoFactorPendingAt);
        Assert.Equal(1, await FailuresAsync(account.User.NormalizedEmail));
    }

    [Fact]
    public async Task Setup_ReturnsTheSecretAndTheUri()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "setup");

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, "/account/two-factor/setup", account.AccessToken,
            new { senha = TwoFactorAccount.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await TwoFactorTestClient.JsonAsync(response);
        var secret = body.GetProperty("segredo").GetString()!;
        Assert.Matches("^[A-Z2-7]{32}$", secret);
        Assert.Equal(
            $"otpauth://totp/Buteco%20Agentes:{Uri.EscapeDataString(account.User.Email)}?secret={secret}&issuer=Buteco%20Agentes&algorithm=SHA1&digits=6&period=30",
            body.GetProperty("otpauthUri").GetString());
    }

    [Fact]
    public async Task Confirm_WithAWrongCode_DoesNotTurnItOn()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "confirm-errado");
        var client = Client();
        var setup = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/setup", account.AccessToken, new { senha = TwoFactorAccount.Password });
        var secret = TwoFactorTestClient.DecodeBase32((await TwoFactorTestClient.JsonAsync(setup)).GetProperty("segredo").GetString()!);

        var response = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/confirm", account.AccessToken,
            new { codigo = TwoFactorTestClient.CodeNow(secret, factory.Clock, stepOffset: 5) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False((await StoredAsync(account.User.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task Confirm_WithTheRightCode_TurnsItOn_ReturnsTenCodes_AndTheNextLoginAsksForTheCode()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "confirm");

        var (_, codes) = await account.EnrollAsync(factory);

        Assert.Equal(10, codes.Distinct().Count());
        Assert.True((await StoredAsync(account.User.Id)).IsTwoFactorEnabled);
        var (login, _) = await TwoFactorTestClient.PasswordStepAsync(Client(), account.User.Email, TwoFactorAccount.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Confirm_AfterTenMinutes_Is400()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "confirm-vencido");
        var client = Client();
        var setup = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/setup", account.AccessToken, new { senha = TwoFactorAccount.Password });
        var secret = TwoFactorTestClient.DecodeBase32((await TwoFactorTestClient.JsonAsync(setup)).GetProperty("segredo").GetString()!);

        factory.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        var response = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/confirm", account.AccessToken,
            new { codigo = TwoFactorTestClient.CodeNow(secret, factory.Clock) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await StoredAsync(account.User.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task Setup_WithTheVerificationOn_Is409_AndTheSecretStays()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "setup-409");
        await account.EnrollAsync(factory);
        var before = (await StoredAsync(account.User.Id)).TwoFactorSecret;

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, "/account/two-factor/setup", account.AccessToken, new { senha = TwoFactorAccount.Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, (await StoredAsync(account.User.Id)).TwoFactorSecret);
    }

    [Fact]
    public async Task TheSecret_NeverComesBackInARead()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "leitura");
        var (secret, _) = await account.EnrollAsync(factory);

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Get, "/account/two-factor", account.AccessToken);

        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Buteco.Api.Users.TwoFactor.Base32.Encode(secret), text, StringComparison.Ordinal);
        Assert.DoesNotContain("otpauth", text, StringComparison.Ordinal);
        var body = await TwoFactorTestClient.JsonAsync(response);
        Assert.True(body.GetProperty("ativa").GetBoolean());
        Assert.Equal(10, body.GetProperty("codigosRestantes").GetInt32());
        Assert.True(body.GetProperty("cadastroDisponivel").GetBoolean());
    }

    [Fact]
    public async Task TheConfirmationCode_DoesNotServeToLogIn()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "codigo-da-confirmacao");
        var (secret, _) = await account.EnrollAsync(factory);
        var client = Client();

        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, account.User.Email, TwoFactorAccount.Password);
        var (response, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, TwoFactorTestClient.CodeNow(secret, factory.Clock));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AfterUsingARecoveryCode_NineRemain()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "nove");
        var (_, codes) = await account.EnrollAsync(factory);
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
        var (secret, oldCodes) = await account.EnrollAsync(factory);
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
    public async Task Remove_WithACode_TurnsItOff_AndTheLoginIs204Again()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "remove");
        var (_, codes) = await account.EnrollAsync(factory);
        var client = Client();

        var response = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/remove", account.AccessToken, new { codigo = codes[0] });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var (login, _) = await TwoFactorTestClient.PasswordStepAsync(client, account.User.Email, TwoFactorAccount.Password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Equal(0, (await TwoFactorTestClient.StateAsync(factory.Services, account.User.Id)).Codes);
    }

    [Fact]
    public async Task Remove_WithAWrongCode_Is401_StaysOn_AndCounts()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "remove-errado");
        var (secret, _) = await account.EnrollAsync(factory);

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, "/account/two-factor/remove", account.AccessToken,
            new { codigo = TwoFactorTestClient.CodeNow(secret, factory.Clock, stepOffset: 5) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True((await StoredAsync(account.User.Id)).IsTwoFactorEnabled);
        Assert.Equal(1, await FailuresAsync(account.User.NormalizedEmail));
    }

    [Fact]
    public async Task RegenerateOrRemove_WithoutTheVerification_Is409()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "sem-verificacao");
        var client = Client();

        Assert.Equal(HttpStatusCode.Conflict,
            (await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/recovery-codes", account.AccessToken, new { codigo = "123456" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/remove", account.AccessToken, new { codigo = "123456" })).StatusCode);
    }
}

/// <summary>
/// O cadastro fechado (spec <c>two-factor-totp</c>, "Cadastro fechado até a tela existir" e
/// "Situação da verificação"; D1). Registro no tasks.md, 8.2 e 8.3: com a opção ignorada no
/// <c>setup</c>, <see cref="Setup_IsClosed_AndNothingIsPending"/> reprova.
/// </summary>
public class TwoFactorEnrollmentClosedTests(TwoFactorEnrollmentClosedTests.Fixture factory) : IClassFixture<TwoFactorEnrollmentClosedTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Identity:TwoFactor:EnrollmentEnabled"] = "false" }));
        }
    }

    private HttpClient Client() => TwoFactorAccount.Anonymous(factory);

    [Fact]
    public async Task Setup_IsClosed_AndNothingIsPending()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "fechado");

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, "/account/two-factor/setup", account.AccessToken, new { senha = TwoFactorAccount.Password });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("O cadastro da verificação em 2 etapas ainda não está disponível.", await TwoFactorTestClient.TitleAsync(response));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == account.User.Id)).TwoFactorPendingAt);
    }

    [Fact]
    public async Task Confirm_IsClosed()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "fechado-confirm");

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, "/account/two-factor/confirm", account.AccessToken, new { codigo = "123456" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Status_WithoutTheVerification_SaysTheEnrollmentIsClosed()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "situacao");

        var body = await TwoFactorTestClient.JsonAsync(
            await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Get, "/account/two-factor", account.AccessToken));

        Assert.False(body.GetProperty("ativa").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("ativadaEm").ValueKind);
        Assert.Equal(0, body.GetProperty("codigosRestantes").GetInt32());
        Assert.False(body.GetProperty("cadastroDisponivel").GetBoolean());
    }

    [Fact]
    public async Task TheVerification_StillAppliesAtLogin()
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("fechado-login"), TwoFactorAccount.Password);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, user.Id);

        var (login, _) = await TwoFactorTestClient.PasswordStepAsync(Client(), user.Email, TwoFactorAccount.Password);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task TheAdminReset_StillWorks()
    {
        var admin = await TwoFactorAccount.CreateAsync(factory, "fechado-admin");
        var other = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("fechado-outro"), TwoFactorAccount.Password);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, other.Id);

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, $"/users/{other.Id}/two-factor/reset", admin.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
