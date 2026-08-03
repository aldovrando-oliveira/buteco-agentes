using System.Text.Json;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Mcp.Security;
using Buteco.Workers.Tests.Mcp.Support;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Workers.Tests.Mcp;

/// <summary>
/// Cobre a change apps-workers-execucao-mcp: descoberta, filtragem por
/// AllowedTools, degradação por servidor, prefixo de nome e ciclo de vida da
/// conexão de <see cref="McpToolSetResolver"/> (ver design.md, Decisions
/// 2/3/4/5). Testa o resolver diretamente (sem AgentExecutionService/
/// ChatClientAgent) — as tabelas mcp_servers/agent_mcp_servers são seedadas
/// via SQL cru (mesmo padrão de SeedAgentAsync em HistorySummarizationTests),
/// porque as entidades mirror são read-only (sem setters públicos).
/// </summary>
public class McpToolSetResolverTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const string EncryptionKey = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=";
    private const string OtherEncryptionKey = "YWxsLXplcm9lcy1rZXktZm9yLXRlc3Rpbmctb25seS0=";

    [Fact]
    public async Task ResolveAsync_ToolAllowedAndOfferedByServer_IsIncluded()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var serverUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(serverUrl, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search"), new FakeMcpTool("write")],
        });

        var mcpServerId = await SeedMcpServerAsync("Servidor A", serverUrl);
        await SeedAgentMcpServerAsync(agentId, mcpServerId, ["search"]);

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        var tool = Assert.Single(toolSet.Tools);
        Assert.Equal("Servidor A__search", tool.Name);
    }

    [Fact]
    public async Task ResolveAsync_ToolNotInAllowedTools_IsExcluded()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var serverUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(serverUrl, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search"), new FakeMcpTool("write")],
        });

        var mcpServerId = await SeedMcpServerAsync("Servidor A", serverUrl);
        await SeedAgentMcpServerAsync(agentId, mcpServerId, ["search"]);

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        Assert.DoesNotContain(toolSet.Tools, tool => tool.Name.EndsWith("__write", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_AllowedToolNoLongerOfferedByServer_IsExcludedWithoutError()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var serverUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        // Servidor só oferece "search" — "delete" foi removida desde que o
        // vínculo foi configurado (drift, ver design.md, Decision 2).
        handler.ConfigureServer(serverUrl, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search")],
        });

        var mcpServerId = await SeedMcpServerAsync("Servidor A", serverUrl);
        await SeedAgentMcpServerAsync(agentId, mcpServerId, ["search", "delete"]);

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        var tool = Assert.Single(toolSet.Tools);
        Assert.Equal("Servidor A__search", tool.Name);
    }

    [Fact]
    public async Task ResolveAsync_EmptyAllowedTools_ResultsInZeroToolsFromThatServer()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var serverUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(serverUrl, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search"), new FakeMcpTool("write")],
        });

        var mcpServerId = await SeedMcpServerAsync("Servidor A", serverUrl);
        await SeedAgentMcpServerAsync(agentId, mcpServerId, []);

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        Assert.Empty(toolSet.Tools);
        // Ainda assim conecta e chama tools/list — sem tratamento especial
        // de "if allowedTools vazio", cai da interseção (Decision 2).
        Assert.Equal(1, handler.CountRequests(serverUrl, "tools/list"));
    }

    [Fact]
    public async Task ResolveAsync_AgentWithNoMcpServerLinked_ResultsInZeroToolsWithoutAnyConnectionAttempt()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var handler = new FakeMcpServerHttpMessageHandler();

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        Assert.Empty(toolSet.Tools);
        Assert.Equal(0, handler.TotalRequestsReceived);
    }

    [Fact]
    public async Task ResolveAsync_InactiveMcpServer_IsExcludedWithoutConnectionAttempt()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var serverUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(serverUrl, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search")],
        });

        var mcpServerId = await SeedMcpServerAsync("Servidor Inativo", serverUrl, isActive: false);
        await SeedAgentMcpServerAsync(agentId, mcpServerId, ["search"]);

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        Assert.Empty(toolSet.Tools);
        Assert.Equal(0, handler.TotalRequestsReceived);
    }

    [Fact]
    public async Task ResolveAsync_UnreachableMcpServer_IsExcludedFromToolSet()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var reachableUrl = UniqueServerUrl();
        var unreachableUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(reachableUrl, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });
        handler.ConfigureServer(unreachableUrl, new FakeMcpServerConfig { Unreachable = true });

        var reachableServerId = await SeedMcpServerAsync("Servidor Alcançável", reachableUrl);
        var unreachableServerId = await SeedMcpServerAsync("Servidor Inalcançável", unreachableUrl);
        await SeedAgentMcpServerAsync(agentId, reachableServerId, ["search"]);
        await SeedAgentMcpServerAsync(agentId, unreachableServerId, ["search"]);

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        var tool = Assert.Single(toolSet.Tools);
        Assert.Equal("Servidor Alcançável__search", tool.Name);
    }

    [Fact]
    public async Task ResolveAsync_AllLinkedMcpServersUnreachable_ResultsInZeroToolsWithoutThrowing()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var urlA = UniqueServerUrl();
        var urlB = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(urlA, new FakeMcpServerConfig { Unreachable = true });
        handler.ConfigureServer(urlB, new FakeMcpServerConfig { Unreachable = true });

        var serverIdA = await SeedMcpServerAsync("Servidor A", urlA);
        var serverIdB = await SeedMcpServerAsync("Servidor B", urlB);
        await SeedAgentMcpServerAsync(agentId, serverIdA, ["search"]);
        await SeedAgentMcpServerAsync(agentId, serverIdB, ["search"]);

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        Assert.Empty(toolSet.Tools);
    }

    [Fact]
    public async Task ResolveAsync_CredentialThatCannotBeDecrypted_DegradesOnlyThatServer()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var reachableUrl = UniqueServerUrl();
        var badCredentialUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(reachableUrl, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });
        handler.ConfigureServer(badCredentialUrl, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });

        var reachableServerId = await SeedMcpServerAsync("Servidor OK", reachableUrl);

        // Credencial cifrada com uma chave diferente da configurada no
        // resolver — decrypt falha com CryptographicException.
        var cipherWithOtherKey = new AesGcmMcpCredentialCipher(Microsoft.Extensions.Options.Options.Create(new McpCryptoOptions { CredentialEncryptionKey = OtherEncryptionKey }));
        var badEncryptedCredential = cipherWithOtherKey.Encrypt("token-qualquer");
        var badCredentialServerId = await SeedMcpServerAsync("Servidor Credencial Ruim", badCredentialUrl, authType: "BearerToken", encryptedCredential: badEncryptedCredential);

        await SeedAgentMcpServerAsync(agentId, reachableServerId, ["search"]);
        await SeedAgentMcpServerAsync(agentId, badCredentialServerId, ["search"]);

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        var tool = Assert.Single(toolSet.Tools);
        Assert.Equal("Servidor OK__search", tool.Name);
        Assert.Equal(0, handler.CountRequests(badCredentialUrl, "initialize"));
    }

    [Fact]
    public async Task ResolveAsync_SameToolNameOnTwoLinkedServers_BothAppearDistinguishableByPrefix()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var urlA = UniqueServerUrl();
        var urlB = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(urlA, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search")],
            ToolCallResult = _ => "resultado do servidor A",
        });
        handler.ConfigureServer(urlB, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search")],
            ToolCallResult = _ => "resultado do servidor B",
        });

        var serverIdA = await SeedMcpServerAsync("Servidor A", urlA);
        var serverIdB = await SeedMcpServerAsync("Servidor B", urlB);
        await SeedAgentMcpServerAsync(agentId, serverIdA, ["search"]);
        await SeedAgentMcpServerAsync(agentId, serverIdB, ["search"]);

        await using var dbContext = CreateDbContext();
        await using var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);

        Assert.Equal(2, toolSet.Tools.Count);
        Assert.Contains(toolSet.Tools, tool => tool.Name == "Servidor A__search");
        Assert.Contains(toolSet.Tools, tool => tool.Name == "Servidor B__search");

        var toolA = Assert.IsAssignableFrom<AIFunction>(toolSet.Tools.Single(tool => tool.Name == "Servidor A__search"));
        var toolB = Assert.IsAssignableFrom<AIFunction>(toolSet.Tools.Single(tool => tool.Name == "Servidor B__search"));

        var resultA = await toolA.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);
        var resultB = await toolB.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        // Cada tool roteia para o servidor MCP correto quando chamada, apesar
        // do nome original idêntico ("search") nos dois.
        Assert.Contains("resultado do servidor A", resultA?.ToString());
        Assert.Contains("resultado do servidor B", resultB?.ToString());
    }

    [Fact]
    public async Task DisposeAsync_ClosesConnections_SubsequentToolCallFails()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        var serverUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(serverUrl, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("ping")] });

        var mcpServerId = await SeedMcpServerAsync("Servidor A", serverUrl);
        await SeedAgentMcpServerAsync(agentId, mcpServerId, ["ping"]);

        await using var dbContext = CreateDbContext();
        var toolSet = await CreateResolver(handler).ResolveAsync(dbContext, agentId, CancellationToken.None);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(toolSet.Tools));

        // Chamável antes do dispose.
        await tool.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        await toolSet.DisposeAsync();

        await Assert.ThrowsAnyAsync<Exception>(
            () => tool.InvokeAsync(new AIFunctionArguments(), CancellationToken.None).AsTask());
    }

    private static string UniqueServerUrl() => $"https://fake-mcp-{Guid.NewGuid():N}.test/mcp";

    private static McpToolSetResolver CreateResolver(FakeMcpServerHttpMessageHandler handler, string encryptionKey = EncryptionKey)
    {
        var cipher = new AesGcmMcpCredentialCipher(Microsoft.Extensions.Options.Options.Create(new McpCryptoOptions { CredentialEncryptionKey = encryptionKey }));
        var transportFactory = new McpTransportFactory(new SingleClientHttpClientFactory(new HttpClient(handler)));
        return new McpToolSetResolver(cipher, transportFactory, NullLogger<McpToolSetResolver>.Instance);
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private async Task SeedAgentAsync(Guid agentId, string agentName = "Atendente", string instructions = "Responda com simpatia.")
    {
        var now = DateTimeOffset.UtcNow;
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {instructions}, {(string?)null}, {(string?)null}, {now}, {now})
             """);
    }

    private async Task<Guid> SeedMcpServerAsync(
        string name,
        string url,
        bool isActive = true,
        string authType = "None",
        string? encryptedCredential = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO mcp_servers ("Id", "Name", "Description", "Url", "AuthType", "EncryptedCredential", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({id}, {name}, {""}, {url}, {authType}, {encryptedCredential}, {isActive}, {now}, {now})
             """);
        return id;
    }

    private async Task SeedAgentMcpServerAsync(Guid agentId, Guid mcpServerId, IReadOnlyList<string> allowedTools)
    {
        var allowedToolsJson = JsonSerializer.Serialize(allowedTools);
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agent_mcp_servers ("AgentId", "McpServerId", allowed_tools)
             VALUES ({agentId}, {mcpServerId}, {allowedToolsJson}::jsonb)
             """);
    }

    private sealed class SingleClientHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
