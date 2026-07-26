using Buteco.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Buteco.Api.Tests.Support;

public sealed class ApiFactoryFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_agents_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs já registrou AppDbContext (AddInfrastructure) apontando para a
        // connection string do appsettings.Development.json. Substituímos aqui, depois
        // que os serviços da aplicação já foram registrados, para garantir que os testes
        // sempre usem o Postgres efêmero do Testcontainers, nunca um Postgres real do
        // ambiente de desenvolvimento.
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
