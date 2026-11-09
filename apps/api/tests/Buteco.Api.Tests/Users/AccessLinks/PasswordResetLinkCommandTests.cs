using System.Net;
using System.Text.RegularExpressions;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// O comando de emergência da senha, pelo binário do <c>apps/api</c> num processo à parte, contra o
/// Postgres do Testcontainers (spec <c>user-access-links</c>, "Comando de emergência para gerar o link
/// de redefinição de senha"; design.md da change comando-password-reset-link, D2 a D5). Um caso por
/// código de saída, um pelo e-mail normalizado e um pelo que sai em cada canal.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 2.3:
/// <list type="bullet">
/// <item>com o comando aceitando quem não é Admin, <see cref="NotAdmin_ExitsFour_AndNoLinkIsWritten"/> reprova;</item>
/// <item>com a escrita antes da base resolvida, <see cref="NoPanelBase_ExitsSeventyEight_AndThePreviousLinkStillWorks"/> reprova;</item>
/// <item>com o link na linha de registro, <see cref="TheStderr_DoesNotCarryTheLink"/> reprova;</item>
/// <item>com o <c>catch</c> geral devolvendo <c>1</c>, <see cref="TheDatabaseDown_ExitsSeventy_AndDoesNotSayTheUserDoesNotExist"/> reprova;</item>
/// <item>com o e-mail comparado sem normalizar, <see cref="AnEmailWithCapitalsAndSpaces_FindsTheUser"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class PasswordResetLinkCommandTests(PasswordResetLinkCommandTests.Fixture factory) : IClassFixture<PasswordResetLinkCommandTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string EventTag = "[1197]";

    /// <summary>A base do painel que o compose de produção entrega ao comando (<c>PublicUrl__BaseUrl</c>).</summary>
    public static readonly IReadOnlyDictionary<string, string> WithPanelBase = new Dictionary<string, string>
    {
        ["PublicUrl__BaseUrl"] = ApiFactoryFixture.PublicBaseUrl,
    };

    private static readonly Regex LinkLine = new(
        "^" + Regex.Escape(ApiFactoryFixture.PublicBaseUrl) + "/redefinir-senha#(?<token>[A-Za-z0-9_-]{43})$", RegexOptions.CultureInvariant);

    private HttpClient Client => SignedInUser.Anonymous(factory);

    public static Task<EmergencyCommandRunner.Result> RunAsync(LocalUsersFixture factory, params string[] args) =>
        EmergencyCommandRunner.RunAsync(factory.PostgresConnectionString, WithPanelBase, args);

    private Task<EmergencyCommandRunner.Result> RunAsync(params string[] args) => RunAsync(factory, args);

    /// <summary>O link da saída <c>0</c>: a única linha não vazia do stdout, no formato do link.</summary>
    public static string LinkOf(EmergencyCommandRunner.Result result)
    {
        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = Assert.Single(lines);
        Assert.Matches(LinkLine, line);
        return line;
    }

    private async Task<bool> LinkWorksAsync(string token) =>
        (await AccessLinkClient.LookupResetAsync(Client, token)).StatusCode == HttpStatusCode.OK;

    private async Task<int> UsersWithAPasswordResetLinkAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .CountAsync(user => user.Id == userId && user.AccessLinkPurpose == AccessLinkPurpose.PasswordReset);
    }

    [Fact]
    public async Task Issued_ExitsZero_PrintsTheLinkOnce_AndTheEffectIsTheRoutes()
    {
        var admin = await SignedInUser.CreateAsync(factory, "comando-senha");
        var previous = await InvitationSeed.PasswordResetAsync(factory.Services, admin.User.Id);
        var before = await SignedInUser.ReloadAsync(factory.Services, admin.User.Id);
        var startedAt = DateTimeOffset.UtcNow;

        var result = await RunAsync(PasswordResetLinkCommand.Name, "--email", admin.User.Email);

        Assert.Equal(PasswordResetLinkCommand.Issued, result.ExitCode);
        var link = LinkOf(result);
        Assert.True(await LinkWorksAsync(AccessLinkClient.TokenOf(link)));
        Assert.False(await LinkWorksAsync(previous));
        var stored = await SignedInUser.ReloadAsync(factory.Services, admin.User.Id);
        Assert.Equal(AccessLinkPurpose.PasswordReset, stored.AccessLinkPurpose);
        Assert.InRange(stored.AccessLinkExpiresAt!.Value - startedAt, AccessLinkToken.PasswordResetLifetime - TimeSpan.FromMinutes(1), AccessLinkToken.PasswordResetLifetime + TimeSpan.FromMinutes(1));
        Assert.Equal(before.SecurityStamp, stored.SecurityStamp);
        Assert.Equal(before.PasswordHash, stored.PasswordHash);
        Assert.Equal([Statuses.Valid], await UserSessionsSeed.AuthorizationStatusesAsync(factory.Services, admin.User.Id));
        await LocalUserSeed.AssertPasswordAcceptedAsync(LocalUserSeed.PostSessionAsync(Client, admin.User.Email, admin.Password), LocalUserSeed.CodeStep);
        Assert.True(await SignedInUser.RenewsAsync(factory, admin.RefreshToken));
    }

    [Fact]
    public async Task TheStderr_DoesNotCarryTheLink()
    {
        var admin = await SignedInUser.CreateAsync(factory, "comando-senha-registro");

        var result = await RunAsync(PasswordResetLinkCommand.Name, "--email", admin.User.Email);

        Assert.Equal(PasswordResetLinkCommand.Issued, result.ExitCode);
        var link = LinkOf(result);
        var token = AccessLinkToken.Hash(AccessLinkClient.TokenOf(link));
        var stamp = (await SignedInUser.ReloadAsync(factory.Services, admin.User.Id)).SecurityStamp;
        Assert.Contains(EventTag, result.Stderr, StringComparison.Ordinal);
        Assert.Contains(admin.User.Id.ToString(), result.Stderr, StringComparison.Ordinal);
        Assert.Contains("Resultado gerado", result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(EventTag, result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessLinkClient.TokenOf(link), result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("redefinir-senha", result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(token), result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(token), result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(stamp, result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmailWithCapitalsAndSpaces_FindsTheUser()
    {
        var joana = await LocalUserSeed.CreateUserAsync(factory.Services, "joana.183@empresa.com", SignedInUser.DefaultPassword);

        var result = await RunAsync(PasswordResetLinkCommand.Name, "--email", "  Joana.183@Empresa.com ");

        Assert.Equal(PasswordResetLinkCommand.Issued, result.ExitCode);
        Assert.True(await LinkWorksAsync(AccessLinkClient.TokenOf(LinkOf(result))));
        Assert.Equal(1, await UsersWithAPasswordResetLinkAsync(joana.Id));
    }

    [Fact]
    public async Task NoUserWithTheEmail_ExitsOne()
    {
        var result = await RunAsync(PasswordResetLinkCommand.Name, "--email", LocalUserSeed.UniqueEmail("ninguem"));

        Assert.Equal(PasswordResetLinkCommand.NoUser, result.ExitCode);
        Assert.Contains("Resultado sem-usuario", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Stdout.Trim());
    }

    [Fact]
    public async Task Deactivated_ExitsTwo_AndNoLinkIsWritten()
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("comando-desativado"), SignedInUser.DefaultPassword);
        await LocalUserSeed.DeactivateAsync(factory.Services, user.Id);

        var result = await RunAsync(PasswordResetLinkCommand.Name, "--email", user.Email);

        Assert.Equal(PasswordResetLinkCommand.Deactivated, result.ExitCode);
        Assert.Contains("Resultado desativado", result.Stderr, StringComparison.Ordinal);
        Assert.Null((await SignedInUser.ReloadAsync(factory.Services, user.Id)).AccessLinkHash);
        Assert.Equal(string.Empty, result.Stdout.Trim());
    }

    [Fact]
    public async Task Invited_ExitsThree_AndTheInvitationLinkStillWorks()
    {
        var inviter = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("comando-convida"), SignedInUser.DefaultPassword);
        var invited = await InvitationSeed.InvitedAsync(factory.Services, inviter.Id);

        var result = await RunAsync(PasswordResetLinkCommand.Name, "--email", invited.User.Email);

        Assert.Equal(PasswordResetLinkCommand.Invited, result.ExitCode);
        Assert.Contains("Resultado convidado", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await AccessLinkClient.LookupInvitationAsync(Client, invited.Token)).StatusCode);
        Assert.Equal(AccessLinkPurpose.Invitation, (await SignedInUser.ReloadAsync(factory.Services, invited.User.Id)).AccessLinkPurpose);
        Assert.Equal(string.Empty, result.Stdout.Trim());
    }

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Reader)]
    public async Task NotAdmin_ExitsFour_AndNoLinkIsWritten(UserRole role)
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("comando-papel"), SignedInUser.DefaultPassword, role: role);

        var result = await RunAsync(PasswordResetLinkCommand.Name, "--email", user.Email);

        Assert.Equal(PasswordResetLinkCommand.NotAdmin, result.ExitCode);
        Assert.Contains("Resultado nao-admin", result.Stderr, StringComparison.Ordinal);
        Assert.Null((await SignedInUser.ReloadAsync(factory.Services, user.Id)).AccessLinkHash);
        Assert.Equal(string.Empty, result.Stdout.Trim());
    }

    [Theory]
    [InlineData(PasswordResetLinkCommand.Name)]
    [InlineData(PasswordResetLinkCommand.Name + " --email")]
    [InlineData(PasswordResetLinkCommand.Name + " --usuario x@exemplo.test")]
    [InlineData(PasswordResetLinkCommand.Name + " --email x@exemplo.test --a-mais")]
    public async Task WrongUsage_ExitsSixtyFour(string commandLine)
    {
        var result = await RunAsync(commandLine.Split(' '));

        Assert.Equal(PasswordResetLinkCommand.Usage, result.ExitCode);
        Assert.Contains("Resultado uso", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyEmail_ExitsSixtyFour()
    {
        var result = await RunAsync(PasswordResetLinkCommand.Name, "--email", "   ");

        Assert.Equal(PasswordResetLinkCommand.Usage, result.ExitCode);
    }

    [Fact]
    public async Task TheDatabaseDown_ExitsSeventy_AndDoesNotSayTheUserDoesNotExist()
    {
        const string password = "senha-do-banco-que-nao-pode-sair";
        var closedPort = $"Host=127.0.0.1;Port=1;Database=buteco_agents;Username=quem;Password={password};Timeout=3";

        var result = await EmergencyCommandRunner.RunAsync(closedPort, WithPanelBase, PasswordResetLinkCommand.Name, "--email", "maria@exemplo.test");

        Assert.Equal(PasswordResetLinkCommand.Failure, result.ExitCode);
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
    public async Task NoPanelBase_ExitsSeventyEight_AndThePreviousLinkStillWorks()
    {
        var admin = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("comando-sem-url"), SignedInUser.DefaultPassword);
        var previous = await InvitationSeed.PasswordResetAsync(factory.Services, admin.Id);
        var noBase = new Dictionary<string, string> { ["PublicUrl__BaseUrl"] = "", ["Identity__PanelBaseUrl"] = "" };

        var result = await EmergencyCommandRunner.RunAsync(factory.PostgresConnectionString, noBase, PasswordResetLinkCommand.Name, "--email", admin.Email);

        Assert.Equal(PasswordResetLinkCommand.NoPanelBase, result.ExitCode);
        Assert.Contains("Resultado sem-url", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("PublicUrl:BaseUrl", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Stdout.Trim());
        Assert.True(await LinkWorksAsync(previous));
    }

    [Fact]
    public async Task AnInvalidPanelBase_ExitsSeventyEight_WithoutTheValue()
    {
        var invalid = new Dictionary<string, string> { ["PublicUrl__BaseUrl"] = "nao-e-url-valor-secreto", ["Identity__PanelBaseUrl"] = "" };

        var result = await EmergencyCommandRunner.RunAsync(factory.PostgresConnectionString, invalid, PasswordResetLinkCommand.Name, "--email", "maria@exemplo.test");

        Assert.Equal(PasswordResetLinkCommand.NoPanelBase, result.ExitCode);
        Assert.DoesNotContain("nao-e-url-valor-secreto", result.Output, StringComparison.Ordinal);
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

            var result = await RunAsync(PasswordResetLinkCommand.Name, "--email", LocalUserSeed.UniqueEmail("sem-migrar"));

            Assert.Equal(PasswordResetLinkCommand.NoUser, result.ExitCode);
            Assert.Contains(last, await db.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1}) ON CONFLICT DO NOTHING", last, product);
        }
    }
}
