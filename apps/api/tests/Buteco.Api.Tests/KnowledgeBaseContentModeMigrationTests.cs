using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Buteco.Api.Tests;

/// <summary>
/// A migração do tipo de conteúdo e da sincronização (design.md da change
/// catalogo-base-sincronizada, "Migration Plan"): as bases existentes viram
/// <c>Manual</c>, e nada mais muda nelas nem nos documentos.
///
/// <para>
/// Classe nova na <see cref="MigrationPostgresCollection"/>, sem contêiner próprio
/// (D12, autorizada pelo mantenedor em 03/10/2026). Cada teste cria o próprio banco.
/// </para>
/// </summary>
[Collection(MigrationPostgresCollection.Name)]
public class KnowledgeBaseContentModeMigrationTests(MigrationPostgresFixture postgres)
{
    private const string MigrationSuffix = "_AddKnowledgeBaseSync";

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

    /// <summary>
    /// A anterior é resolvida pela lista ordenada de migrações, nunca fixada pelo nome,
    /// pelo mesmo motivo de <c>KnowledgeDocumentEventsMigrationTests</c>.
    /// </summary>
    private static (string Previous, string Target) ResolveMigrationPair(AppDbContext dbContext)
    {
        var migrations = dbContext.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(id => id.EndsWith(MigrationSuffix, StringComparison.Ordinal));

        Assert.True(index >= 0, $"A migração '*{MigrationSuffix}' não está na lista do AppDbContext.");
        Assert.True(index > 0, $"A migração '*{MigrationSuffix}' é a primeira: não há anterior para partir dela.");

        return (migrations[index - 1], migrations[index]);
    }

    // As colunas que existiam antes, serializadas: a comparação de antes e depois é
    // sobre o texto, e pega qualquer coluna que a migração tenha tocado.
    private const string BasesSnapshotSql = """
        select json_agg(row_to_json(b) order by b."Id")::text from (
            select "Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt" from knowledge_bases) b;
        """;

    private const string DocumentsSnapshotSql = """
        select json_agg(row_to_json(d) order by d."Id")::text from (
            select "Id", "KnowledgeBaseId", "Title", "SourceType", "ExtractedText", "IndexingStatus", "IndexedAt",
                   "FailureReason", "ContentRevision", "ContentHash", "FragmentCount", "IndexingAttempts",
                   "LastAttemptAt", "CreatedAt", "UpdatedAt", "ContentLengthBytes"
            from knowledge_documents) d;
        """;

    [Fact]
    public async Task Migration_TurnsExistingBasesIntoManualAndChangesNothingElse()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var dbContext = NewDbContext(connectionString);
        var (previous, target) = ResolveMigrationPair(dbContext);
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(previous);

        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        await ExecuteAsync(connectionString, $"""
            INSERT INTO knowledge_bases ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ('{activeId}', 'Ativa', 'Descrição ativa.', TRUE, '2026-09-01T10:00:00Z', '2026-09-02T10:00:00Z'),
                   ('{inactiveId}', 'Inativa', 'Descrição inativa.', FALSE, '2026-09-03T10:00:00Z', '2026-09-04T10:00:00Z');
            INSERT INTO knowledge_documents
                ("Id", "KnowledgeBaseId", "Title", "SourceType", "ExtractedText", "IndexingStatus",
                 "ContentRevision", "ContentHash", "CreatedAt", "UpdatedAt")
            VALUES
                ('{Guid.NewGuid()}', '{activeId}', 'Doc 1', 'markdown', '# Um', 'Pending', 1, NULL, now(), now()),
                ('{Guid.NewGuid()}', '{inactiveId}', 'Doc 2', 'markdown', '# Dois', 'Indexed', 3, 'abc', now(), now());
            """);
        var basesBefore = await ScalarAsync<string>(connectionString, BasesSnapshotSql);
        var documentsBefore = await ScalarAsync<string>(connectionString, DocumentsSnapshotSql);
        var eventsBefore = await ScalarAsync<long>(connectionString, "select count(*) from knowledge_document_events;");

        await migrator.MigrateAsync(target);

        Assert.Equal(basesBefore, await ScalarAsync<string>(connectionString, BasesSnapshotSql));
        Assert.Equal(documentsBefore, await ScalarAsync<string>(connectionString, DocumentsSnapshotSql));
        Assert.Equal(eventsBefore, await ScalarAsync<long>(connectionString, "select count(*) from knowledge_document_events;"));

        Assert.Equal(2, await ScalarAsync<long>(connectionString, """
            select count(*) from knowledge_bases
            where "ContentMode" = 'Manual'
              and "SyncProvider" is null and "SyncFolderId" is null and "SyncFolderName" is null and "SyncFolderUrl" is null
              and "LastSyncCompletedAt" is null and "LastSyncFinishedAt" is null and "LastSyncErrorCode" is null
              and "LastSyncErrorDetail" is null and "SyncFailingSince" is null and "SyncIgnoredFiles" is null;
            """));
        Assert.Equal(2, await ScalarAsync<long>(connectionString, """
            select count(*) from knowledge_documents
            where "KnowledgeBaseContentMode" = 'Manual' and "ExternalRef" is null and "ExternalVersion" is null;
            """));
    }

    [Fact]
    public async Task Migration_ReplacesTheSimpleForeignKeyByTheCompositeOne()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var dbContext = NewDbContext(connectionString))
        {
            await dbContext.Database.MigrateAsync();
        }

        var foreignKeys = await ScalarAsync<string>(connectionString, """
            select string_agg(pg_get_constraintdef(c.oid), ' | ' order by c.conname)
            from pg_constraint c
            join pg_class t on t.oid = c.conrelid
            where t.relname = 'knowledge_documents' and c.contype = 'f';
            """);

        // Uma FK só para a base, composta, em Restrict (D4): a simples foi substituída.
        Assert.Equal(
            "FOREIGN KEY (\"KnowledgeBaseId\", \"KnowledgeBaseContentMode\") REFERENCES knowledge_bases(\"Id\", \"ContentMode\") ON DELETE RESTRICT",
            foreignKeys);
    }
}
