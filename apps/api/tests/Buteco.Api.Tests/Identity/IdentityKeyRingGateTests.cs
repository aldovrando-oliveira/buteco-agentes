using System.Net;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O gate e o aquecimento do anel (spec <c>oidc-issuer</c>, "Endpoints do emissor
/// respondem 503 enquanto as chaves não carregam"; design.md da change emissor-oidc-api,
/// D6). Cada teste sobe o próprio host, porque o que se afirma é o que acontece antes e
/// depois da primeira carga.
/// </summary>
public class IdentityKeyRingGateTests
{
    /// <summary>Host que sobe antes de o banco ser migrado, com o aquecimento a cada 200 ms.</summary>
    public sealed class UnmigratedFactory : ApiFactoryFixture
    {
        public IdentityIssuerLogCapture Logs { get; } = new();

        protected override bool MigratesOnInitialize => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IdentityKeyRotationSchedule>();
                services.AddSingleton(new IdentityKeyRotationSchedule { WarmupRetryInterval = TimeSpan.FromMilliseconds(200) });
            });
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        }
    }

    [Fact]
    public async Task BeforeMigration_EveryRouteIs503WithRetryAfter_AndAfterMigrationTheJwksAnswersWithoutRestart()
    {
        await using var factory = new UnmigratedFactory();
        await ((IAsyncLifetime)factory).InitializeAsync();

        var health = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.Equal("5", health.Headers.GetValues("Retry-After").Single());

        await factory.MigrateAsync();

        var jwks = await factory.CreateClient().GetAsync("/.well-known/jwks");
        Assert.Equal(HttpStatusCode.OK, jwks.StatusCode);
        await ((IAsyncLifetime)factory).DisposeAsync();
    }

    [Fact]
    public async Task DatabaseMigratedBeforeStart_TheWarmupLoads_AndTheFirstHealthDoesNotLoad()
    {
        await using var migrated = new ApiFactoryFixture();
        await ((IAsyncLifetime)migrated).InitializeAsync();

        // Um host novo contra o banco já migrado: é o caso de produção, com o migrator antes.
        await using var factory = migrated.WithWebHostBuilder(_ => { });
        var ring = factory.Services.GetRequiredService<IdentityKeyRing>();
        await WaitForAsync(() => ring.IsLoaded);
        var loadsBeforeTheRequest = ring.LoadCount;

        var health = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(loadsBeforeTheRequest, ring.LoadCount);
        await ((IAsyncLifetime)migrated).DisposeAsync();
    }

    [Fact]
    public async Task WarmupFails_ThenRetries_AndLoadsWithoutAnyRequest()
    {
        await using var factory = new UnmigratedFactory();
        await ((IAsyncLifetime)factory).InitializeAsync();
        var ring = factory.Services.GetRequiredService<IdentityKeyRing>();

        await WaitForAsync(() => factory.Logs.Lines.Any(line => line.EventId == IdentityKeyRotationService.WarmupFailedEvent));
        Assert.False(ring.IsLoaded);

        await factory.MigrateAsync();

        await WaitForAsync(() => ring.IsLoaded);
        await ((IAsyncLifetime)factory).DisposeAsync();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "a condição esperada não aconteceu em 15 s");
            await Task.Delay(50);
        }
    }
}
