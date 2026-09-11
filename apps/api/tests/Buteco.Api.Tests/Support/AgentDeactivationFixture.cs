using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Support;

public sealed class AgentDeactivationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_deactivation_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public FakeTaskJobPublisher TaskJobPublisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestAuthentication.ConfigOverrides));

        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql(_postgres.GetConnectionString()));

            // Substitui o publisher real do RabbitMQ por um spy in-memory: o
            // que este teste precisa provar é ausência de publish para agente
            // inativo, e isso fica mais direto verificando chamadas ao spy do
            // que fazer polling de fila vazia contra um broker real.
            var publisherDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITaskJobPublisher));
            if (publisherDescriptor is not null)
            {
                services.Remove(publisherDescriptor);
            }

            services.AddSingleton<ITaskJobPublisher>(TaskJobPublisher);
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        TestAuthentication.AttachOperatorToken(client, Services);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
