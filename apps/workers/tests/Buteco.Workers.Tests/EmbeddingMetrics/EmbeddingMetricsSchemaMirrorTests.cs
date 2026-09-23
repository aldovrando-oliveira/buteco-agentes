using Buteco.Workers.EmbeddingMetrics.Entities;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Entities;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Workers.Tests.EmbeddingMetrics;

/// <summary>
/// O espelho de schema das duas tabelas de métrica de embedding. Os dois
/// <c>AppDbContext</c> são sincronizados por <b>disciplina</b>, não por schema
/// compartilhado — este teste é o que transforma disciplina em verificação, no
/// par com <c>EmbeddingMetricsMigrationTests</c> de <c>apps/api</c>.
///
/// <para>
/// Nota operacional: as migrações de <c>apps/workers</c> <b>só</b> rodam aqui,
/// contra container descartável. Contra banco real quem migra é <c>apps/api</c>.
/// </para>
///
/// <para>
/// <b>Fora da <c>WorkerHostCollection</c></b>, no precedente de
/// <c>KnowledgeSchemaMirrorTests</c> e <c>ExecutionMetricsSchemaMirrorTests</c>:
/// não precisa de host, e a 15ª classe da coleção obrigaria a recalibrar o
/// limiar de carga da suíte (convenção 22).
/// </para>
/// </summary>
public class EmbeddingMetricsSchemaMirrorTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
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
    public async Task Migrations_CreateBothEmbeddingMetricsTables()
    {
        var tables = await ScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema = 'public' " +
            "and table_name in ('knowledge_indexing_attempts', 'embedding_calls');");

        Assert.Equal(2, tables);
    }

    // Só as colunas cuja nulidade CARREGA DECISÃO.
    //
    // `InputTokens` é o caso da convenção 13 — NOT NULL obrigaria a gravar zero
    // onde o provedor não reportou nada, e zero é uma afirmação diferente.
    //
    // Os dois pais são anuláveis porque exatamente UM deles é preenchido por
    // linha, e qual depende de `Purpose` (design.md, D9): a linha de indexação
    // não tem task, e a de busca não tem tentativa.
    //
    // `FailurePhase` e `FragmentCount` são nulos quando a pergunta não se aplica
    // — indexou (não há fase), ou não indexou (não há contagem).
    [Theory]
    [InlineData("embedding_calls", "InputTokens", "bigint", "YES")]
    [InlineData("embedding_calls", "KnowledgeIndexingAttemptId", "uuid", "YES")]
    [InlineData("embedding_calls", "TaskId", "text", "YES")]
    [InlineData("embedding_calls", "HttpStatus", "integer", "YES")]
    [InlineData("knowledge_indexing_attempts", "FailurePhase", "text", "YES")]
    [InlineData("knowledge_indexing_attempts", "FragmentCount", "integer", "YES")]
    // E o par: as que NÃO podem ser nulas, porque toda linha as tem. Sem este
    // lado, o guarda acima passaria com a tabela inteira anulável.
    [InlineData("embedding_calls", "Purpose", "text", "NO")]
    [InlineData("embedding_calls", "KnowledgeBaseId", "uuid", "NO")]
    [InlineData("embedding_calls", "Provider", "text", "NO")]
    [InlineData("embedding_calls", "Model", "text", "NO")]
    [InlineData("embedding_calls", "Dimensions", "integer", "NO")]
    [InlineData("embedding_calls", "InputCount", "integer", "NO")]
    [InlineData("embedding_calls", "Failed", "boolean", "NO")]
    [InlineData("embedding_calls", "DurationMs", "double precision", "NO")]
    [InlineData("knowledge_indexing_attempts", "KnowledgeDocumentId", "uuid", "NO")]
    [InlineData("knowledge_indexing_attempts", "KnowledgeBaseId", "uuid", "NO")]
    [InlineData("knowledge_indexing_attempts", "Attempt", "integer", "NO")]
    [InlineData("knowledge_indexing_attempts", "MaxAttempts", "integer", "NO")]
    [InlineData("knowledge_indexing_attempts", "Outcome", "text", "NO")]
    [InlineData("knowledge_indexing_attempts", "StartedAt", "timestamp with time zone", "NO")]
    [InlineData("knowledge_indexing_attempts", "EndedAt", "timestamp with time zone", "NO")]
    public async Task Columns_HaveTheExpectedTypeAndNullability(
        string table, string column, string expectedType, string expectedNullable)
    {
        var actual = await ScalarAsync<string>(
            $"select data_type || '|' || is_nullable from information_schema.columns " +
            $"where table_name = '{table}' and column_name = '{column}';");

        Assert.Equal($"{expectedType}|{expectedNullable}", actual);
    }

    /// <summary>
    /// <b>Não há coluna de tokens de saída</b>, e a ausência é decisão (D10):
    /// embedding não produz saída, e uma coluna sempre nula convida a somá-la.
    /// Um guarda de ausência, porque o defeito a pegar é alguém acrescentá-la
    /// "por simetria com <c>provider_calls</c>".
    /// </summary>
    [Fact]
    public async Task EmbeddingCalls_HasNoOutputTokenColumn()
    {
        var outputColumns = await ScalarAsync<long>(
            "select count(*) from information_schema.columns " +
            "where table_name = 'embedding_calls' and column_name in ('OutputTokens', 'CachedInputTokens');");

        Assert.Equal(0, outputColumns);
    }

    /// <summary>
    /// O par ESTRUTURAL do guarda de snapshot (D9; convenção 15, quinta forma),
    /// no mesmo molde do de <c>ExecutionMetricsSchemaMirrorTests</c>. O
    /// comportamental — excluir uma base não apaga a métrica — passa também com
    /// uma FK que ninguém usa para join; o que impede a consulta de ler o
    /// catálogo de hoje para o consumo de ontem é não haver relação para seguir.
    /// Afirmado no modelo do EF, que é de onde a migração sai.
    /// </summary>
    [Fact]
    public void EfModel_HasNoForeignKeyFromEmbeddingMetricsToTheCatalog()
    {
        using var dbContext = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

        var catalogTypes = new[] { typeof(KnowledgeBase), typeof(KnowledgeDocument) };

        var foreignKeysToCatalog = new[] { typeof(EmbeddingCall), typeof(KnowledgeIndexingAttempt) }
            .SelectMany(type => dbContext.Model.FindEntityType(type)!.GetForeignKeys())
            .Where(foreignKey => catalogTypes.Contains(foreignKey.PrincipalEntityType.ClrType))
            .ToList();

        Assert.Empty(foreignKeysToCatalog);
    }

    /// <summary>
    /// E o par positivo: as <b>duas</b> FKs que devem existir são para tabelas de
    /// métrica, e valem por construção (D7/D9). Sem este lado, o guarda acima
    /// passaria com uma tabela sem FK nenhuma.
    /// </summary>
    [Fact]
    public void EfModel_HasTheTwoForeignKeysToMetricsParents()
    {
        using var dbContext = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

        var principals = dbContext.Model.FindEntityType(typeof(EmbeddingCall))!
            .GetForeignKeys()
            .Select(foreignKey => foreignKey.PrincipalEntityType.ClrType)
            .ToList();

        Assert.Contains(typeof(KnowledgeIndexingAttempt), principals);
        Assert.Contains(typeof(Buteco.Workers.ExecutionMetrics.Entities.TaskExecution), principals);
    }
}
