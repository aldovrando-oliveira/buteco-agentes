using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

/// <summary>
/// Exercita a migration `AddAgentMcpServerAllowedTools` (Decision 5 do
/// design.md da change backend-mcp-selecao-tools) contra um
/// `AgentMcpServer` real criado antes da coluna `allowed_tools` existir —
/// diferente dos demais testes, aplica as migrations em duas etapas (até
/// `AddMcpServerCatalog`, depois até a última) para simular um vínculo
/// pré-existente no ambiente, em vez de migrar direto para o schema atual.
/// </summary>
public class AgentMcpServerAllowedToolsMigrationTests : IAsyncLifetime
{
    private const string PreviousMigrationId = "20260802163110_AddMcpServerCatalog";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_migration_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ExistingAgentMcpServerBinding_MigratesToEmptyAllowedTools()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseButecoAgentsNpgsql(_postgres.GetConnectionString())
            .Options;

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigrationId, CancellationToken.None);
        }

        var agentId = Guid.NewGuid();
        var mcpServerId = Guid.NewGuid();

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "CreatedAt", "UpdatedAt")
                VALUES ({agentId}, 'Agente Pré-Existente', 'Instruções.', true, now(), now())
                """);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO mcp_servers ("Id", "Name", "Description", "Url", "AuthType", "IsActive", "CreatedAt", "UpdatedAt")
                VALUES ({mcpServerId}, 'MCP Pré-Existente', 'Descrição', 'https://mcp.example.com', 'None', true, now(), now())
                """);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agent_mcp_servers ("AgentId", "McpServerId") VALUES ({agentId}, {mcpServerId})
                """);
        }

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(null!, CancellationToken.None);
        }

        await using (var dbContext = new AppDbContext(options))
        {
            var binding = await dbContext.AgentMcpServers
                .SingleAsync(binding => binding.AgentId == agentId && binding.McpServerId == mcpServerId);

            Assert.Empty(binding.AllowedTools);
        }
    }
}
