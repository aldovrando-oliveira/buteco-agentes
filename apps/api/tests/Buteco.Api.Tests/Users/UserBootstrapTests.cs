using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O bootstrap do primeiro Admin (spec <c>local-users</c>, "Primeiro Admin criado pelo
/// bootstrap, sem sobrescrever nada"; design.md da change usuarios-locais-login-emissor, D2).
///
/// <para>
/// Verificado contra o defeito (convenção 15): com o bootstrap reaplicando a configuração
/// quando já há usuário (troca do e-mail e do hash), o caso
/// <see cref="WithUsers_AnotherConfigurationChangesNothing_AndLogsIgnored"/> reprova
/// (registro no tasks.md, 4.2).
/// </para>
/// </summary>
public class UserBootstrapTests(UserBootstrapFixture factory) : IClassFixture<UserBootstrapFixture>
{
    private async Task<User> WaitForBootstrapUserAsync()
    {
        User? user = null;
        await UserBootstrapFixture.WaitForAsync(async () =>
        {
            using var scope = factory.Services.CreateScope();
            user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleOrDefaultAsync();
            return user is not null;
        });
        return user!;
    }

    [Fact]
    public async Task EmptyDatabase_TheWarmupCreatesTheUser_WithTheMarkedLegacyHash_AndLogsWithoutTheHash()
    {
        var user = await WaitForBootstrapUserAsync();

        Assert.Equal(UserBootstrapFixture.BootstrapEmail, user.Email);
        Assert.Equal("admin", user.Name);
        Assert.True(user.IsActive);

        // O outro caso desta classe faz o primeiro login e refaz o hash; antes dele, a
        // linha guarda o PBKDF2 da configuração com o marcador.
        if (user.PasswordHashIsBootstrapLegacy)
        {
            Assert.Equal(TestAuthentication.ComputeKnownOperatorPasswordHash(), user.PasswordHash);
        }
        else
        {
            Assert.StartsWith("$argon2id$", user.PasswordHash);
        }

        var created = factory.Logs.Lines.Single(line => line.EventId == UserBootstrapper.CreatedEvent);
        Assert.Equal(LogLevel.Information, created.Level);
        Assert.Contains(user.Id.ToString(), created.Message);
        Assert.DoesNotContain(TestAuthentication.ComputeKnownOperatorPasswordHash(), string.Join('\n', factory.Logs.Lines.Select(line => line.Message)));
    }

    [Fact]
    public async Task TheFirstLogin_RehashesWithArgon2id_AndTheNextLoginStillWorks()
    {
        await WaitForBootstrapUserAsync();
        var client = factory.CreateClient(new() { HandleCookies = false });

        var first = await LocalUserSeed.PostSessionAsync(client, UserBootstrapFixture.BootstrapEmail, TestAuthentication.KnownOperatorPassword);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, first.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync();
            Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", user.PasswordHash);
            Assert.False(user.PasswordHashIsBootstrapLegacy);
        }

        var second = await LocalUserSeed.PostSessionAsync(client, UserBootstrapFixture.BootstrapEmail, TestAuthentication.KnownOperatorPassword);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task WithUsers_AnotherConfigurationChangesNothing_AndLogsIgnored()
    {
        var before = await WaitForBootstrapUserAsync();

        using var scope = factory.Services.CreateScope();
        var other = new UserBootstrapper(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Microsoft.Extensions.Options.Options.Create(new UserBootstrapOptions
            {
                BootstrapAdminEmail = "outro@exemplo.test",
                OperatorPasswordHash = "100000.b3V0cm8tc2Fs.b3V0cm8taGFzaA==",
            }),
            TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<ILogger<UserBootstrapper>>());

        Assert.Equal(UserBootstrapOutcome.Ignored, await other.RunAsync(CancellationToken.None));

        using var check = factory.Services.CreateScope();
        var after = await check.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Contains(factory.Logs.Lines, line => line.EventId == UserBootstrapper.IgnoredEvent);
    }
}

/// <summary>Sem <c>Auth:BootstrapAdminEmail</c>: só o aviso; e duas execuções simultâneas criam um usuário só.</summary>
public class UserBootstrapWithoutEmailTests(UserBootstrapWithoutEmailFixture factory) : IClassFixture<UserBootstrapWithoutEmailFixture>
{
    [Fact]
    public async Task WithoutTheEmail_OnlyWarns_ThenTwoConcurrentBootstrapsCreateOneUser()
    {
        await UserBootstrapFixture.WaitForAsync(() => Task.FromResult(
            factory.Logs.Lines.Any(line => line.EventId == UserBootstrapper.MissingConfigurationEvent)));

        var warning = factory.Logs.Lines.First(line => line.EventId == UserBootstrapper.MissingConfigurationEvent);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("Auth:BootstrapAdminEmail", warning.Message);

        using (var scope = factory.Services.CreateScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync());
        }

        // O app atende normalmente.
        Assert.Equal(System.Net.HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health")).StatusCode);

        var options = Microsoft.Extensions.Options.Options.Create(new UserBootstrapOptions
        {
            BootstrapAdminEmail = "paralelo@exemplo.test",
            OperatorPasswordHash = TestAuthentication.ComputeKnownOperatorPasswordHash(),
        });

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var bootstrapper = new UserBootstrapper(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                options,
                TimeProvider.System,
                scope.ServiceProvider.GetRequiredService<ILogger<UserBootstrapper>>());
            return await bootstrapper.RunAsync(CancellationToken.None);
        }));

        Assert.Equal([UserBootstrapOutcome.Created, UserBootstrapOutcome.Ignored], outcomes.Order());
        using var check = factory.Services.CreateScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }
}
