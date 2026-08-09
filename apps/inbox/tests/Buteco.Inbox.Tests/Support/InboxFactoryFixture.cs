using Buteco.Inbox.Agents;
using Buteco.Inbox.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Buteco.Inbox.Tests.Support;

public sealed class InboxFactoryFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_inbox_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public FakeAgentApiHttpMessageHandler AgentApiHandler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inbox:CredentialEncryptionKey"] = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=",
                ["Api:BaseUrl"] = "http://apps-api.test",
            }));

        // Program.cs já registrou AppDbContext (AddInfrastructure) apontando para a
        // connection string do appsettings.Development.json. Substituímos aqui, depois
        // que os serviços da aplicação já foram registrados, para garantir que os testes
        // sempre usem o Postgres efêmero do Testcontainers, nunca um Postgres real do
        // ambiente de desenvolvimento (mesmo mecanismo de ApiFactoryFixture, apps/api).
        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));

            // Substitui o HttpMessageHandler primário do cliente nomeado
            // "AgentReferenceValidator" pelo fake, sem rede real (mesmo
            // mecanismo de FakeMcpServerHttpMessageHandler, apps/api).
            services.AddHttpClient(AgentReferenceValidator.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => AgentApiHandler);
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
