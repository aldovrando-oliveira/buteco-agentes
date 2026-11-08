using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.TwoFactor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// O comando de emergência das 2 etapas, pelo binário do <c>apps/api</c> num processo à parte,
/// contra o Postgres do Testcontainers (spec <c>two-factor-totp</c>, "Comando de emergência para
/// redefinir a verificação"; design.md da change verificacao-2-etapas-obrigatoria, D6). Um caso
/// por código de saída e um pelo registro.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 5.2:
/// <list type="bullet">
/// <item>com o comando sem a troca do stamp, <see cref="Reset_ExitsZero_EndsTheSessions_AndTheNextLoginEnrolls"/> reprova;</item>
/// <item>com o comando aceitando o desativado, <see cref="Deactivated_ExitsTwo_AndNothingChanges"/> reprova;</item>
/// <item>com o <c>catch</c> geral devolvendo <c>1</c>, <see cref="TheDatabaseDown_ExitsSeventy_AndDoesNotSayTheUserDoesNotExist"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class TwoFactorEmergencyResetTests(TwoFactorEmergencyResetTests.Fixture factory) : IClassFixture<TwoFactorEmergencyResetTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string EventTag = "[1196]";

    private Task<EmergencyCommandRunner.Result> RunAsync(params string[] args) =>
        EmergencyCommandRunner.RunAsync(factory.PostgresConnectionString, args);

    private async Task<User> StoredAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == userId);
    }

    /// <summary>Uma conta com a verificação, cadastrada pelo login, e duas sessões vivas.</summary>
    private async Task<TwoFactorAccount> AccountWithTwoSessionsAsync(string prefix)
    {
        var account = await TwoFactorAccount.CreateAsync(factory, prefix);
        var client = TwoFactorAccount.Anonymous(factory);
        await TwoFactorTestClient.TokensAsync(client, factory.Services,
            await IdentityIssuerUserFlow.SessionCookieAsync(client, factory.Services, account.User.Email, TwoFactorAccount.Password));
        return account;
    }

    [Fact]
    public async Task Reset_ExitsZero_EndsTheSessions_AndTheNextLoginEnrolls()
    {
        var account = await AccountWithTwoSessionsAsync("emergencia");
        var stampBefore = (await StoredAsync(account.User.Id)).SecurityStamp;

        var result = await RunAsync(TwoFactorEmergencyCommand.Name, "--email", account.User.Email);

        Assert.Equal(TwoFactorEmergencyCommand.ResetDone, result.ExitCode);
        var stored = await StoredAsync(account.User.Id);
        Assert.False(stored.IsTwoFactorEnabled);
        Assert.Null(stored.TwoFactorSecret);
        Assert.NotEqual(stampBefore, stored.SecurityStamp);
        Assert.Equal(0, (await TwoFactorTestClient.StateAsync(factory.Services, account.User.Id)).Codes);
        Assert.Equal([Statuses.Revoked, Statuses.Revoked], await UserSessionsSeed.AuthorizationStatusesAsync(factory.Services, account.User.Id));
        await LocalUserSeed.AssertPasswordAcceptedAsync(
            LocalUserSeed.PostSessionAsync(TwoFactorAccount.Anonymous(factory), account.User.Email, TwoFactorAccount.Password), LocalUserSeed.EnrollmentStep);

        Assert.Contains(EventTag, result.Output, StringComparison.Ordinal);
        Assert.Contains(account.User.Id.ToString(), result.Output, StringComparison.Ordinal);
        Assert.Contains("2 sessões encerradas", result.Output, StringComparison.Ordinal);
        Assert.Contains("Resultado redefinida", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Base32.Encode(account.Secret), result.Output, StringComparison.Ordinal);
        Assert.All(account.RecoveryCodes, code => Assert.DoesNotContain(code, result.Output, StringComparison.Ordinal));
        Assert.DoesNotContain(stampBefore, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.SecurityStamp, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoUserWithTheEmail_ExitsOne()
    {
        var email = LocalUserSeed.UniqueEmail("ninguem");

        var result = await RunAsync(TwoFactorEmergencyCommand.Name, "--email", email);

        Assert.Equal(TwoFactorEmergencyCommand.NoUser, result.ExitCode);
        Assert.Contains(EventTag, result.Output, StringComparison.Ordinal);
        Assert.Contains("Resultado sem-usuario", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivated_ExitsTwo_AndNothingChanges()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "emergencia-desativado");
        await LocalUserSeed.DeactivateAsync(factory.Services, account.User.Id);
        var before = await StoredAsync(account.User.Id);

        var result = await RunAsync(TwoFactorEmergencyCommand.Name, "--email", account.User.Email);

        Assert.Equal(TwoFactorEmergencyCommand.Deactivated, result.ExitCode);
        var after = await StoredAsync(account.User.Id);
        Assert.True(after.IsTwoFactorEnabled);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Contains("Resultado desativado", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutTheVerification_ExitsThree_AndTheStampStays()
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("emergencia-sem"), TwoFactorAccount.Password);

        var result = await RunAsync(TwoFactorEmergencyCommand.Name, "--email", user.Email);

        Assert.Equal(TwoFactorEmergencyCommand.NotEnabled, result.ExitCode);
        Assert.Equal(user.SecurityStamp, (await StoredAsync(user.Id)).SecurityStamp);
        Assert.Contains("Resultado sem-verificacao", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TwoFactorEmergencyCommand.Name)]
    [InlineData(TwoFactorEmergencyCommand.Name + " --email")]
    [InlineData(TwoFactorEmergencyCommand.Name + " --usuario x@exemplo.test")]
    public async Task WrongUsage_ExitsSixtyFour(string commandLine)
    {
        var result = await RunAsync(commandLine.Split(' '));

        Assert.Equal(TwoFactorEmergencyCommand.Usage, result.ExitCode);
        Assert.Contains("Resultado uso", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDatabaseDown_ExitsSeventy_AndDoesNotSayTheUserDoesNotExist()
    {
        const string password = "senha-do-banco-que-nao-pode-sair";
        var closedPort = $"Host=127.0.0.1;Port=1;Database=buteco_agents;Username=quem;Password={password};Timeout=3";

        var result = await EmergencyCommandRunner.RunAsync(closedPort, TwoFactorEmergencyCommand.Name, "--email", "maria@exemplo.test");

        Assert.Equal(TwoFactorEmergencyCommand.Failure, result.ExitCode);
        Assert.Contains(EventTag, result.Output, StringComparison.Ordinal);
        Assert.Contains("Resultado falha", result.Output, StringComparison.Ordinal);
        Assert.Contains("Exception", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("nenhum usuário", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("sem-usuario", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(password, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Port=1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCommand_DoesNotApplyMigrations()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var last = (await db.Database.GetAppliedMigrationsAsync()).Last();
        var product = await db.Database.SqlQueryRaw<string>(
            "SELECT \"ProductVersion\" AS \"Value\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {0}", last).SingleAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {0}", last);
        try
        {
            Assert.Contains(last, await db.Database.GetPendingMigrationsAsync());

            var result = await RunAsync(TwoFactorEmergencyCommand.Name, "--email", LocalUserSeed.UniqueEmail("sem-migrar"));

            Assert.Equal(TwoFactorEmergencyCommand.NoUser, result.ExitCode);
            Assert.Contains(last, await db.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1}) ON CONFLICT DO NOTHING", last, product);
        }
    }
}
