using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Buteco.Api.Tests;

/// <summary>
/// Exercita a migração `AddMcpServerAuthHeaderName` (change
/// mcp-autenticacao-header-customizado, Migration Plan) contra um
/// <c>McpServer</c> gravado ANTES da coluna existir: a coluna é aditiva e
/// anulável, sem backfill, então a linha antiga continua lida com o mesmo tipo,
/// a mesma credencial e <c>AuthHeaderName</c> nulo.
/// </summary>
[Collection(MigrationPostgresCollection.Name)]
public class McpServerAuthHeaderNameMigrationTests(MigrationPostgresFixture postgres)
{
    private const string PreviousMigrationId = "20261004020943_AddKnowledgeIndexingRequests";

    [Fact]
    public async Task ExistingBearerTokenServer_IsReadWithNullAuthHeaderName_AfterMigration()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseButecoAgentsNpgsql(await postgres.CreateDatabaseAsync())
            .Options;

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigrationId, CancellationToken.None);
        }

        var mcpServerId = Guid.NewGuid();

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO mcp_servers ("Id", "Name", "Description", "Url", "AuthType", "EncryptedCredential", "IsActive", "CreatedAt", "UpdatedAt")
                VALUES ({mcpServerId}, 'MCP Pré-Existente', 'Descrição', 'https://mcp.example.com', 'BearerToken', 'cifra-anterior', true, now(), now())
                """);
        }

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(null!, CancellationToken.None);
        }

        await using (var dbContext = new AppDbContext(options))
        {
            var mcpServer = await dbContext.McpServers.SingleAsync(server => server.Id == mcpServerId);

            Assert.Equal(McpServerAuthType.BearerToken, mcpServer.AuthType);
            Assert.Equal("cifra-anterior", mcpServer.EncryptedCredential);
            Assert.Null(mcpServer.AuthHeaderName);
        }
    }
}
