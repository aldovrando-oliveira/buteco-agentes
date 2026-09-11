using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

/// <summary>
/// Exercita a migration `AddAgentDescriptionAndSkills` contra um `Agent`
/// real criado antes das colunas `Description`/`skills` existirem —
/// mesmo padrão de `AgentMcpServerAllowedToolsMigrationTests`: aplica as
/// migrations em duas etapas (até `AddAgentMcpServerAllowedTools`, depois
/// até a última) para simular um agente pré-existente no ambiente, em vez
/// de migrar direto para o schema atual. Cobre só o comportamento da
/// migration em si (schema, via `DbContext` direto) — o caminho real de
/// serialização via HTTP é coberto por
/// `AgentEndpointsTests.GetAgentById_LegacyAgentWithoutDescriptionOrSkills_ReturnsNullAndEmptyList`
/// e `ListAgents_IncludesLegacyAgentWithNullDescriptionAndEmptySkills`.
/// </summary>
public class AgentDescriptionAndSkillsMigrationTests : IAsyncLifetime
{
    private const string PreviousMigrationId = "20260803010705_AddAgentMcpServerAllowedTools";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_migration_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ExistingAgent_MigratesToNullDescriptionAndEmptySkills()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseButecoAgentsNpgsql(_postgres.GetConnectionString())
            .Options;

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigrationId, CancellationToken.None);
        }

        var agentId = Guid.NewGuid();

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "CreatedAt", "UpdatedAt")
                VALUES ({agentId}, 'Agente Pré-Existente', 'Instruções.', true, now(), now())
                """);
        }

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(null!, CancellationToken.None);
        }

        await using (var dbContext = new AppDbContext(options))
        {
            var agent = await dbContext.Agents.SingleAsync(agent => agent.Id == agentId);

            Assert.Null(agent.Description);
            Assert.Empty(agent.Skills);
        }
    }
}
