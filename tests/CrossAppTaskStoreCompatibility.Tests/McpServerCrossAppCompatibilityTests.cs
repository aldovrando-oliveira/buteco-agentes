using CrossAppTaskStoreCompatibility.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using ApiAgent = Buteco.Api.Agents.Entities.Agent;
using ApiAgentMcpServer = Buteco.Api.AgentMcpBindings.Entities.AgentMcpServer;
using ApiAuthType = Buteco.Api.McpServers.Entities.McpServerAuthType;
using ApiCipher = Buteco.Api.McpServers.Security.AesGcmMcpCredentialCipher;
using ApiCryptoOptions = Buteco.Api.McpServers.Security.McpCryptoOptions;
using ApiDbContext = Buteco.Api.Infrastructure.AppDbContext;
using ApiMcpServer = Buteco.Api.McpServers.Entities.McpServer;
using WorkerCipher = Buteco.Workers.Mcp.Security.AesGcmMcpCredentialCipher;
using WorkerCryptoOptions = Buteco.Workers.Mcp.Security.McpCryptoOptions;
using WorkerDbContext = Buteco.Workers.Infrastructure.AppDbContext;
using WorkerResolver = Buteco.Workers.Mcp.McpToolSetResolver;
using WorkerTransportFactory = Buteco.Workers.Mcp.McpTransportFactory;

namespace CrossAppTaskStoreCompatibility.Tests;

/// <summary>
/// Acordo entre <c>apps/api</c> e <c>apps/workers</c> sobre <c>mcp_servers</c>
/// (change mcp-autenticacao-header-customizado, D5, convenções 11 e 12): a
/// linha é gravada pelo contexto, pela entidade e pela cifra REAIS de
/// <c>apps/api</c>, e lida pelo contexto e pelo resolvedor REAIS de
/// <c>apps/workers</c> — nunca uma linha montada pelo teste com a configuração
/// dos dois lados. O banco é migrado só por <c>apps/api</c>, como em produção.
/// </summary>
public class McpServerCrossAppCompatibilityTests : IAsyncLifetime
{
    // Valor fictício de teste, o mesmo nos dois lados — em produção é a variável
    // MCP_CREDENTIAL_ENCRYPTION_KEY, byte-idêntica em api e workers.
    private const string EncryptionKey = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_cross_app_mcp_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    private IServiceScopeFactory _apiScopeFactory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var apiServices = new ServiceCollection();
        apiServices.AddDbContext<ApiDbContext>(options => options.UseButecoAgentsNpgsql(_postgres.GetConnectionString()));
        _apiScopeFactory = apiServices.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        using var scope = _apiScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApiDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // Guarda da tarefa 8.2. Afirma o NOME lido, não só "não lançou": com o
    // conversor tolerante de apps/workers (D5), um membro que faltasse lá
    // materializaria como null sem exceção — e é esse o caso que o guarda existe
    // para pegar.
    [Fact]
    public async Task EveryAuthTypeWrittenByApi_IsReadByWorkersWithTheSameName()
    {
        var written = new Dictionary<Guid, ApiAuthType>();

        using (var scope = _apiScopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            foreach (var authType in Enum.GetValues<ApiAuthType>())
            {
                var server = new ApiMcpServer(
                    $"Servidor {authType}",
                    "Descrição",
                    "https://mcp.example.com",
                    authType,
                    authType == ApiAuthType.None ? null : ApiCipher().Encrypt("SEGREDO-acordo"),
                    authType == ApiAuthType.CustomHeader ? "X-API-TOKEN" : null);
                dbContext.McpServers.Add(server);
                written[server.Id] = authType;
            }

            await dbContext.SaveChangesAsync();
        }

        await using var workerContext = CreateWorkerContext();
        var read = await workerContext.McpServers.AsNoTracking()
            .Where(server => written.Keys.Contains(server.Id))
            .ToDictionaryAsync(server => server.Id, server => server.AuthType);

        Assert.Equal(written.Count, read.Count);
        foreach (var (id, authType) in written)
        {
            Assert.Equal(authType.ToString(), read[id]?.ToString());
        }
    }

    // Tarefa 8.3: a credencial cifrada por apps/api, decifrada por apps/workers,
    // sai no header configurado, e nada vai em Authorization.
    [Fact]
    public async Task CustomHeaderRowWrittenByApi_IsResolvedByWorkers_AndTheServerReceivesTheOriginalCredential()
    {
        Guid agentId;
        using (var scope = _apiScopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var agent = new ApiAgent("Agente MCP cruzado", "Instruções.", "openai", "gpt-5.6-sol", null, []);
            var server = new ApiMcpServer(
                "Servidor Header Cruzado",
                "Descrição",
                "https://mcp-cruzado.example.com/mcp",
                ApiAuthType.CustomHeader,
                ApiCipher().Encrypt("SEGREDO-cruzado"),
                "X-API-TOKEN");
            dbContext.Agents.Add(agent);
            dbContext.McpServers.Add(server);
            dbContext.AgentMcpServers.Add(new ApiAgentMcpServer(agent.Id, server.Id, ["search"]));
            await dbContext.SaveChangesAsync();
            agentId = agent.Id;
        }

        var handler = new CrossAppFakeMcpServerHandler(["search"]);
        var resolver = new WorkerResolver(
            new WorkerCipher(Microsoft.Extensions.Options.Options.Create(new WorkerCryptoOptions { CredentialEncryptionKey = EncryptionKey })),
            new WorkerTransportFactory(new SingleClientHttpClientFactory(new HttpClient(handler))),
            NullLogger<WorkerResolver>.Instance);

        await using var workerContext = CreateWorkerContext();
        await using (var toolSet = await resolver.ResolveAsync(workerContext, agentId, CancellationToken.None))
        {
            Assert.Single(toolSet.Tools);
        }

        var received = handler.ReceivedHeaders;
        Assert.NotEmpty(received);
        Assert.All(received, headers => Assert.Equal("SEGREDO-cruzado", headers["X-API-TOKEN"]));
        Assert.All(received, headers => Assert.False(headers.ContainsKey("Authorization")));
    }

    private static ApiCipher ApiCipher() =>
        new(Microsoft.Extensions.Options.Options.Create(new ApiCryptoOptions { CredentialEncryptionKey = EncryptionKey }));

    private WorkerDbContext CreateWorkerContext() =>
        new(new DbContextOptionsBuilder<WorkerDbContext>().UseButecoAgentsNpgsql(_postgres.GetConnectionString()).Options);

    private sealed class SingleClientHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
