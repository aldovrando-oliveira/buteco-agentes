using Buteco.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Igual a <see cref="ApiFactoryFixture"/>, mas limpando explicitamente a
/// <c>ApiKey</c> dos três provedores — independente do que
/// <c>appsettings.Development.json</c> tiver localmente (esse arquivo é
/// ajustado livremente por quem desenvolve, não deveria ser pré-requisito
/// de teste) — para exercitar o caso de <c>GET /providers</c> com nenhum
/// provedor disponível.
/// </summary>
public sealed class NoProvidersConfiguredFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_agents_no_providers_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = string.Empty,
                ["Anthropic:ApiKey"] = string.Empty,
                ["Gemini:ApiKey"] = string.Empty,
            });
            config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));
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
