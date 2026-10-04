using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Buteco.Api.Tests;

/// <summary>
/// A migração do pedido de indexação (design.md da change indexacao-sem-job-orfao):
/// a recuperação dos documentos que já estão sem indexação enfileirada (D6) e a FK
/// em cascata para o documento (D1).
///
/// <para>
/// Na <see cref="MigrationPostgresCollection"/>, com banco por teste: nenhuma fonte
/// de contêiner nova.
/// </para>
/// </summary>
[Collection(MigrationPostgresCollection.Name)]
public class KnowledgeIndexingRequestsMigrationTests(MigrationPostgresFixture postgres)
{
    private const string MigrationSuffix = "_AddKnowledgeIndexingRequests";

    private static AppDbContext NewDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(connectionString).Options);

    private static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A anterior é resolvida pela lista de migrações, nunca fixada pelo nome.</summary>
    private static (string Previous, string Target) ResolveMigrationPair(AppDbContext dbContext)
    {
        var migrations = dbContext.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(id => id.EndsWith(MigrationSuffix, StringComparison.Ordinal));

        Assert.True(index >= 0, $"A migração '*{MigrationSuffix}' não está na lista do AppDbContext.");
        Assert.True(index > 0, $"A migração '*{MigrationSuffix}' é a primeira: não há anterior para partir dela.");

        return (migrations[index - 1], migrations[index]);
    }

    [Fact]
    public async Task Migration_CreatesOneRequestPerPendingDocument_AndNoneForTheOtherStates()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var dbContext = NewDbContext(connectionString);
        var (previous, target) = ResolveMigrationPair(dbContext);
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(previous);

        var baseId = Guid.NewGuid();
        var pendingNew = Guid.NewGuid();
        var pendingEdited = Guid.NewGuid();
        var indexing = Guid.NewGuid();
        var indexed = Guid.NewGuid();
        var failed = Guid.NewGuid();
        await ExecuteAsync(connectionString, $"""
            INSERT INTO knowledge_bases ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ('{baseId}', 'Base anterior', 'Descrição.', TRUE, now(), now());
            INSERT INTO knowledge_documents
                ("Id", "KnowledgeBaseId", "Title", "SourceType", "ExtractedText", "IndexingStatus",
                 "ContentRevision", "CreatedAt", "UpdatedAt")
            VALUES
                ('{pendingNew}', '{baseId}', 'Pendente nunca indexado', 'markdown', '# Um', 'Pending', 1, now(), now()),
                ('{pendingEdited}', '{baseId}', 'Pendente editado', 'markdown', '# Dois', 'Pending', 4, now(), now()),
                ('{indexing}', '{baseId}', 'Indexando', 'markdown', '# Três', 'Indexing', 2, now(), now()),
                ('{indexed}', '{baseId}', 'Indexado', 'markdown', '# Quatro', 'Indexed', 3, now(), now()),
                ('{failed}', '{baseId}', 'Falhou', 'markdown', '# Cinco', 'Failed', 5, now(), now());
            """);

        await migrator.MigrateAsync(target);

        var requests = await ScalarAsync<string>(connectionString, """
            select string_agg("KnowledgeDocumentId"::text || ':' || "ContentRevision", ',' order by "ContentRevision")
            from knowledge_indexing_requests;
            """);

        // Exatamente um pedido por Pending, com a revisão corrente — e nenhum para
        // Indexing, Indexed e Failed.
        Assert.Equal($"{pendingNew}:1,{pendingEdited}:4", requests);
    }

    [Fact]
    public async Task Migration_CascadesFromTheDocument()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var dbContext = NewDbContext(connectionString))
        {
            await dbContext.Database.MigrateAsync();
        }

        var foreignKeys = await ScalarAsync<string>(connectionString, """
            select string_agg(ccu.table_name || ':' || rc.delete_rule, ',' order by ccu.table_name)
            from information_schema.table_constraints tc
            join information_schema.referential_constraints rc on rc.constraint_name = tc.constraint_name
            join information_schema.constraint_column_usage ccu on ccu.constraint_name = tc.constraint_name
            where tc.constraint_type = 'FOREIGN KEY' and tc.table_name = 'knowledge_indexing_requests';
            """);

        Assert.Equal("knowledge_documents:CASCADE", foreignKeys);
    }
}
