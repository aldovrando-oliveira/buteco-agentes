using Buteco.Workers.Infrastructure;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// Os dois <c>AppDbContext</c> são sincronizados por disciplina, não por schema
/// compartilhado — este teste é o que transforma "disciplina" em verificação
/// (design.md, R9). Aplica as migrações de <c>apps/workers</c> num Postgres
/// limpo do Testcontainers e afirma a forma do schema resultante.
///
/// Nota operacional: as migrações de <c>apps/workers</c> **só** rodam aqui,
/// contra container descartável. Contra banco real quem migra é <c>apps/api</c>
/// (design.md, D15).
/// </summary>
public class KnowledgeSchemaMirrorTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.Postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    [Fact]
    public async Task Migrations_CreateBothKnowledgeTables()
    {
        var tables = await ScalarAsync<long>(
            "select count(*) from information_schema.tables " +
            "where table_schema = 'public' and table_name in ('knowledge_bases', 'knowledge_documents');");

        Assert.Equal(2, tables);
    }

    [Theory]
    [InlineData("knowledge_bases", "Name", "text", "NO")]
    [InlineData("knowledge_bases", "Description", "text", "NO")]
    [InlineData("knowledge_bases", "IsActive", "boolean", "NO")]
    [InlineData("knowledge_documents", "Title", "text", "NO")]
    [InlineData("knowledge_documents", "SourceType", "text", "NO")]
    [InlineData("knowledge_documents", "ExtractedText", "text", "NO")]
    [InlineData("knowledge_documents", "ContentRevision", "integer", "NO")]
    [InlineData("knowledge_documents", "IndexedAt", "timestamp with time zone", "YES")]
    [InlineData("knowledge_documents", "FailureReason", "text", "YES")]
    public async Task Columns_HaveTheExpectedTypeAndNullability(
        string table, string column, string expectedType, string expectedNullable)
    {
        var actual = await ScalarAsync<string>(
            $"select data_type || '|' || is_nullable from information_schema.columns " +
            $"where table_name = '{table}' and column_name = '{column}';");

        Assert.Equal($"{expectedType}|{expectedNullable}", actual);
    }

    // IndexingStatus persistido como texto, nunca inteiro ordinal — vale no
    // banco, não só no fio (convenção 12).
    [Fact]
    public async Task IndexingStatus_IsStoredAsText()
    {
        var dataType = await ScalarAsync<string>(
            "select data_type from information_schema.columns " +
            "where table_name = 'knowledge_documents' and column_name = 'IndexingStatus';");

        Assert.Equal("text", dataType);
    }

    // A coluna gerada precisa existir com a MESMA expressão de apps/api: se o
    // espelho a declarasse como coluna comum, a divergência só apareceria
    // quando alguém comparasse os dois schemas à mão.
    [Fact]
    public async Task ContentLengthBytes_IsAStoredGeneratedColumnOverOctetLength()
    {
        var isGenerated = await ScalarAsync<string>(
            "select is_generated from information_schema.columns " +
            "where table_name = 'knowledge_documents' and column_name = 'ContentLengthBytes';");

        Assert.Equal("ALWAYS", isGenerated);

        var expression = await ScalarAsync<string>(
            "select generation_expression from information_schema.columns " +
            "where table_name = 'knowledge_documents' and column_name = 'ContentLengthBytes';");

        Assert.Contains("octet_length", expression!);
        Assert.Contains("ExtractedText", expression!);
    }

    // Restrict, NÃO cascata (design.md, D6): base não tem exclusão, e a FK
    // obriga quem for adicioná-la um dia a decidir o destino dos documentos.
    [Fact]
    public async Task ForeignKeyFromDocumentToBase_UsesRestrictNotCascade()
    {
        var deleteRule = await ScalarAsync<string>(
            "select rc.delete_rule from information_schema.referential_constraints rc " +
            "join information_schema.table_constraints tc on tc.constraint_name = rc.constraint_name " +
            "where tc.table_name = 'knowledge_documents';");

        Assert.Equal("RESTRICT", deleteRule);
    }

    [Fact]
    public async Task KnowledgeBaseId_IsIndexed()
    {
        var indexes = await ScalarAsync<long>(
            "select count(*) from pg_indexes " +
            "where tablename = 'knowledge_documents' and indexdef like '%KnowledgeBaseId%';");

        Assert.True(indexes >= 1, "a FK para a base precisa de índice próprio");
    }

    // --- Vínculo agente x base (change knowledge-base-vinculo-agente, R1) ---

    [Fact]
    public async Task Migrations_CreateTheAgentKnowledgeBindingTable()
    {
        var tables = await ScalarAsync<long>(
            "select count(*) from information_schema.tables " +
            "where table_schema = 'public' and table_name = 'agent_knowledge_bases';");

        Assert.Equal(1, tables);
    }

    [Theory]
    [InlineData("AgentId", "uuid", "NO")]
    [InlineData("KnowledgeBaseId", "uuid", "NO")]
    public async Task AgentKnowledgeBindingColumns_HaveTheExpectedTypeAndNullability(
        string column, string expectedType, string expectedNullable)
    {
        var actual = await ScalarAsync<string>(
            $"select data_type || '|' || is_nullable from information_schema.columns " +
            $"where table_name = 'agent_knowledge_bases' and column_name = '{column}';");

        Assert.Equal($"{expectedType}|{expectedNullable}", actual);
    }

    // O vínculo não tem coluna extra por decisão (design.md, D2) — nada de
    // análogo a AgentMcpServer.AllowedTools. A asserção é NEGATIVA de
    // propósito: é ela que impede o espelho de ganhar coluna que apps/api não
    // tem, que é a forma da divergência difícil de enxergar.
    [Fact]
    public async Task AgentKnowledgeBindingTable_HasExactlyTheTwoKeyColumns()
    {
        var columns = await ScalarAsync<string>(
            "select string_agg(column_name, ',' order by column_name) " +
            "from information_schema.columns where table_name = 'agent_knowledge_bases';");

        Assert.Equal("AgentId,KnowledgeBaseId", columns);
    }

    [Fact]
    public async Task AgentKnowledgeBinding_HasCompositePrimaryKeyOnBothColumns()
    {
        var keyColumns = await ScalarAsync<string>(
            "select string_agg(kcu.column_name, ',' order by kcu.ordinal_position) " +
            "from information_schema.table_constraints tc " +
            "join information_schema.key_column_usage kcu on kcu.constraint_name = tc.constraint_name " +
            "where tc.table_name = 'agent_knowledge_bases' and tc.constraint_type = 'PRIMARY KEY';");

        Assert.Equal("AgentId,KnowledgeBaseId", keyColumns);
    }

    // Cascade nas DUAS FKs (design.md, D10) — e deliberadamente diferente do
    // RESTRICT que knowledge_documents usa para a base. Trocar um pelo outro
    // no mapeamento espelhado precisa reprovar aqui.
    [Fact]
    public async Task AgentKnowledgeBindingForeignKeys_UseCascade()
    {
        var deleteRules = await ScalarAsync<string>(
            "select string_agg(distinct rc.delete_rule, ',') " +
            "from information_schema.referential_constraints rc " +
            "join information_schema.table_constraints tc on tc.constraint_name = rc.constraint_name " +
            "where tc.table_name = 'agent_knowledge_bases';");

        Assert.Equal("CASCADE", deleteRules);

        var foreignKeyCount = await ScalarAsync<long>(
            "select count(*) from information_schema.table_constraints " +
            "where table_name = 'agent_knowledge_bases' and constraint_type = 'FOREIGN KEY';");

        Assert.Equal(2, foreignKeyCount);
    }

    // O modelo espelhado não pode ter drift em relação às migrações: se
    // divergisse, o EF acusaria mudanças pendentes de modelo.
    [Fact]
    public async Task MirroredModel_HasNoPendingModelChanges()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString())
            .Options;

        await using var dbContext = new AppDbContext(options);

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
    }
}
