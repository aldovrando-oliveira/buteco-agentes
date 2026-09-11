using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Connectivity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Igual a <see cref="ApiFactoryFixture"/>, mas substitui o
/// <see cref="HttpMessageHandler"/> primário do cliente HTTP nomeado usado
/// por <see cref="McpConnectionTester"/> por <see cref="FakeMcpServerHttpMessageHandler"/>
/// — permite exercitar o handler/command real de teste de conexão MCP sem
/// nenhuma rede real (ver Decision 2 do design.md da change
/// backend-mcp-catalogo-vinculo).
/// </summary>
public sealed class McpConnectionTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_mcp_connection_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public FakeMcpServerHttpMessageHandler McpServerHandler { get; } = new();

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

            services.AddHttpClient(McpConnectionTester.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => McpServerHandler);
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
