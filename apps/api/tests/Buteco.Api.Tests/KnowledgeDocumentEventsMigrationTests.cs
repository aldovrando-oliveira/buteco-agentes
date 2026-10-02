using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Buteco.Api.Tests;

/// <summary>
/// A migração do histórico de documentos (design.md da change
/// historico-documentos-base): que ela não cria evento retroativo (D10), e a forma
/// que ela deixa no banco (D1, D3, D5).
///
/// <para>
/// Divide o contêiner da <see cref="MigrationPostgresCollection"/> com
/// <c>RejectionMetricsMigrationTests</c> (D12), e cada teste cria o próprio banco
/// — um para o estado parado na migração anterior, outro para o fim.
/// </para>
/// </summary>
[Collection(MigrationPostgresCollection.Name)]
public class KnowledgeDocumentEventsMigrationTests(MigrationPostgresFixture postgres)
{
    private const string MigrationSuffix = "_AddKnowledgeDocumentEvents";

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
    /// A anterior é RESOLVIDA pela lista ordenada de migrações, nunca fixada pelo
    /// nome: fixá-la quebraria o teste, ou o faria testar o par errado, no dia em
    /// que uma migração entrar entre as duas. E falha de forma explícita se a
    /// migração do histórico sumir da lista ou virar a primeira.
    /// </summary>
    private static (string Previous, string Target) ResolveMigrationPair(AppDbContext dbContext)
    {
        var migrations = dbContext.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(id => id.EndsWith(MigrationSuffix, StringComparison.Ordinal));

        Assert.True(index >= 0, $"A migração '*{MigrationSuffix}' não está na lista do AppDbContext.");
        Assert.True(index > 0, $"A migração '*{MigrationSuffix}' é a primeira: não há anterior para partir dela.");

        return (migrations[index - 1], migrations[index]);
    }

    [Fact]
    public async Task Migration_CreatesNoEventForDocumentsThatAlreadyExisted()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var dbContext = NewDbContext(connectionString);
        var (previous, target) = ResolveMigrationPair(dbContext);
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(previous);

        // Base e dois documentos como existiam antes do histórico.
        var baseId = Guid.NewGuid();
        await ExecuteAsync(connectionString, $"""
            INSERT INTO knowledge_bases ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ('{baseId}', 'Base anterior', 'Descrição.', TRUE, now(), now());
            INSERT INTO knowledge_documents
                ("Id", "KnowledgeBaseId", "Title", "SourceType", "ExtractedText", "IndexingStatus",
                 "ContentRevision", "CreatedAt", "UpdatedAt")
            VALUES
                ('{Guid.NewGuid()}', '{baseId}', 'Doc 1', 'markdown', '# Um', 'Pending', 1, now(), now()),
                ('{Guid.NewGuid()}', '{baseId}', 'Doc 2', 'markdown', '# Dois', 'Indexed', 3, now(), now());
            """);
        Assert.Equal(2, await ScalarAsync<long>(connectionString, "select count(*) from knowledge_documents;"));

        await migrator.MigrateAsync(target);

        Assert.Equal(0, await ScalarAsync<long>(connectionString, "select count(*) from knowledge_document_events;"));
    }

    [Fact]
    public async Task Migration_CascadesFromTheBaseAndHasNoForeignKeyToTheDocument()
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
            where tc.constraint_type = 'FOREIGN KEY' and tc.table_name = 'knowledge_document_events';
            """);

        // Uma FK só, para a base, em cascata (D3) — e nenhuma para
        // knowledge_documents, ou o evento de exclusão morreria com o documento (D1).
        Assert.Equal("knowledge_bases:CASCADE", foreignKeys);
    }

    [Fact]
    public async Task Migration_HasTheChangeDetailCheckAndTheRouteIndex()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var dbContext = NewDbContext(connectionString))
        {
            await dbContext.Database.MigrateAsync();
        }

        var checks = await ScalarAsync<long>(connectionString, """
            select count(*) from pg_constraint c
            join pg_class t on t.oid = c.conrelid
            where t.relname = 'knowledge_document_events' and c.contype = 'c'
              and c.conname = 'CK_knowledge_document_events_change_detail';
            """);
        Assert.Equal(1, checks);

        // A ordem das colunas do índice importa (D1): base, depois a chave da
        // ordem da rota.
        var indexDefinition = await ScalarAsync<string>(connectionString, """
            select indexdef from pg_indexes
            where tablename = 'knowledge_document_events'
              and indexname = 'IX_knowledge_document_events_KnowledgeBaseId_OccurredAt_Id';
            """);
        Assert.NotNull(indexDefinition);
        Assert.Contains("(\"KnowledgeBaseId\", \"OccurredAt\", \"Id\")", indexDefinition);
    }
}
