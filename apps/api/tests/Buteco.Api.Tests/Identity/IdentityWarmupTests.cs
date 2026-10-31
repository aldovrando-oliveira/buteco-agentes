using System.Net;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.Bootstrap;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O aquecimento em passos independentes (spec <c>local-users</c>, "Falha do bootstrap é
/// tentada de novo no ciclo seguinte"; design.md da change usuarios-locais-login-emissor,
/// D3.13): o bootstrap falha uma vez, o anel carrega mesmo assim, e no ciclo seguinte o
/// usuário existe.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com o laço de antes, que só repete enquanto
/// o anel não carregou, o usuário nunca é criado e o teste reprova (registro no tasks.md,
/// 4.3).
/// </para>
/// </summary>
public class IdentityWarmupTests(IdentityWarmupTests.FailingBootstrapFixture factory)
    : IClassFixture<IdentityWarmupTests.FailingBootstrapFixture>
{
    /// <summary>O bootstrap real, que lança na primeira chamada.</summary>
    public sealed class FailOnceBootstrapper(UserBootstrapper inner, FailOnceBootstrapper.Counter counter) : IUserBootstrapper
    {
        public sealed class Counter
        {
            public int Calls;
        }

        public Task<UserBootstrapOutcome> RunAsync(CancellationToken cancellationToken) =>
            Interlocked.Increment(ref counter.Calls) == 1
                ? throw new InvalidOperationException("falha injetada no bootstrap")
                : inner.RunAsync(cancellationToken);
    }

    /// <summary>O catálogo real, que lança na primeira chamada.</summary>
    public sealed class FailOnceCatalog(Buteco.Api.Identity.Clients.IdentityClientCatalog inner, FailOnceCatalog.Counter counter)
        : Buteco.Api.Identity.Clients.IIdentityClientCatalog
    {
        public sealed class Counter
        {
            public int Calls;
        }

        public Task SyncAsync(CancellationToken cancellationToken) =>
            Interlocked.Increment(ref counter.Calls) == 1
                ? throw new InvalidOperationException("falha injetada no catálogo")
                : inner.SyncAsync(cancellationToken);
    }

    public sealed class FailingBootstrapFixture : UserBootstrapFixture
    {
        public FailOnceBootstrapper.Counter Counter { get; } = new();

        public FailOnceCatalog.Counter CatalogCounter { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(Counter);
                services.AddScoped<UserBootstrapper>();
                services.AddScoped<IUserBootstrapper, FailOnceBootstrapper>();
                services.AddSingleton(CatalogCounter);
                services.AddScoped<Buteco.Api.Identity.Clients.IdentityClientCatalog>();
                services.AddScoped<Buteco.Api.Identity.Clients.IIdentityClientCatalog, FailOnceCatalog>();
            });
        }
    }

    [Fact]
    public async Task TheCatalogFailsOnce_TheRingAndTheBootstrapConcludeAnyway_AndTheNextCycleSyncs()
    {
        await UserBootstrapFixture.WaitForAsync(() => Task.FromResult(
            factory.Logs.Lines.Any(line => line.EventId == IdentityKeyRotationService.CatalogFailedEvent)));

        await UserBootstrapFixture.WaitForAsync(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>()
                .FindByClientIdAsync(Buteco.Api.Identity.Clients.IdentityClients.PanelClientId) is not null;
        });

        Assert.True(factory.CatalogCounter.Calls >= 2);
        Assert.True(factory.Services.GetRequiredService<IdentityKeyRing>().IsLoaded);
    }

    [Fact]
    public async Task TheBootstrapFailsOnce_TheRingLoadsAnyway_AndTheNextCycleCreatesTheUser()
    {
        await UserBootstrapFixture.WaitForAsync(() => Task.FromResult(
            factory.Logs.Lines.Any(line => line.EventId == IdentityKeyRotationService.BootstrapFailedEvent &&
                line.Message.Contains("bootstrap", StringComparison.Ordinal))));

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health")).StatusCode);
        Assert.True(factory.Services.GetRequiredService<IdentityKeyRing>().IsLoaded);

        await UserBootstrapFixture.WaitForAsync(async () =>
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync();
        });

        Assert.True(factory.Counter.Calls >= 2);
    }
}
