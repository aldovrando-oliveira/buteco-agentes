using System.Net;
using Buteco.Api.Identity;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.TwoFactor;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// A chave de cifra trocada (spec <c>two-factor-totp</c>, "Segredo que não decifra não bloqueia
/// os códigos de recuperação"; design.md da change verificacao-2-etapas-totp, D5).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 11.1: com a decifração
/// fora do <c>try</c>, <see cref="TheAppCode_Is500_LogsTheEvent_AndCountsNothing"/> reprova.
/// </para>
/// </summary>
public class TwoFactorKeyChangeTests(TwoFactorKeyChangeTests.Fixture factory) : IClassFixture<TwoFactorKeyChangeTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture
    {
        public IdentityIssuerLogCapture Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        }
    }

    private const string OtherKey = "b3V0cmEtY2hhdmUtZGUtdGVzdGUtZG8tdG90cC0tLS0=";

    /// <summary>Um usuário com a verificação ligada, com o segredo cifrado por outra chave.</summary>
    private async Task<(Buteco.Api.Users.User User, byte[] Secret, IReadOnlyList<string> Codes)> UserWithAnotherKeyAsync(string prefix)
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail(prefix), TwoFactorAccount.Password);
        var enabled = await TwoFactorTestClient.SeedEnabledAsync(factory.Services, user.Id);
        var other = new TwoFactorSecretCipher(Microsoft.Extensions.Options.Options.Create(new IdentityIssuerOptions { TwoFactorEncryptionKey = OtherKey }));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(candidate => candidate.Id == user.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.TwoFactorSecret, other.Protect(user.Id, enabled.Secret)));
        }

        TwoFactorTestClient.NextStep(factory.Clock);
        return (user, enabled.Secret, enabled.RecoveryCodes);
    }

    [Fact]
    public async Task TheAppCode_Is500_LogsTheEvent_AndCountsNothing()
    {
        var (user, secret, _) = await UserWithAnotherKeyAsync("chave-trocada");
        var client = TwoFactorAccount.Anonymous(factory);
        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, TwoFactorAccount.Password);

        var (response, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, TwoFactorTestClient.CodeNow(secret, factory.Clock));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(
            "Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador.",
            await TwoFactorTestClient.TitleAsync(response));
        var logged = Assert.Single(factory.Logs.Lines, line => line.EventId.Id == TwoFactorVerifier.SecretUnreadableEvent.Id && line.Message.Contains(user.Id.ToString()));
        Assert.Equal(LogLevel.Error, logged.Level);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles.AsNoTracking()
            .SingleOrDefaultAsync(throttle => throttle.NormalizedEmail == user.NormalizedEmail));
    }

    [Fact]
    public async Task ARecoveryCode_StillCompletesTheLogin()
    {
        var (user, _, codes) = await UserWithAnotherKeyAsync("chave-trocada-recuperacao");
        var client = TwoFactorAccount.Anonymous(factory);
        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, TwoFactorAccount.Password);

        var (response, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, codes[0]);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
