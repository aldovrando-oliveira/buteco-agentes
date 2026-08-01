using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Igual a <see cref="AgentDeactivationFixture"/> (Postgres efêmero +
/// <see cref="FakeTaskJobPublisher"/> como spy), usada para os testes de
/// rejeição de <c>SendMessage</c> por falta de provider utilizável (agente
/// sem Provider/Model ou com Provider que deixou de estar configurado).
/// </summary>
public sealed class AgentProviderRejectionFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_agents_provider_rejection_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public FakeTaskJobPublisher TaskJobPublisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));

            var publisherDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITaskJobPublisher));
            if (publisherDescriptor is not null)
            {
                services.Remove(publisherDescriptor);
            }

            services.AddSingleton<ITaskJobPublisher>(TaskJobPublisher);
        });
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
