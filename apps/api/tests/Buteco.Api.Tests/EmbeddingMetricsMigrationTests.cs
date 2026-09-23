using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Buteco.Api.Tests;

/// <summary>
/// A migração <c>AddEmbeddingMetrics</c> é a que roda em produção — o espelho de
/// <c>apps/workers</c> só roda contra Testcontainers. Por isso a forma do schema
/// é afirmada aqui, e de novo do lado de lá
/// (<c>EmbeddingMetricsSchemaMirrorTests</c>): os dois <c>AppDbContext</c> são
/// sincronizados por disciplina, e é o par de testes que transforma a disciplina
/// em verificação. Mesmo molde de <see cref="ExecutionMetricsMigrationTests"/>.
/// </summary>
public class EmbeddingMetricsMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_embedding_metrics_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseButecoAgentsNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    [Fact]
    public async Task Migration_CreatesTheTwoEmbeddingMetricsTables()
    {
        var tables = await ScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema = 'public' " +
            "and table_name in ('knowledge_indexing_attempts', 'embedding_calls');");

        Assert.Equal(2, tables);
    }

    /// <summary>
    /// <b>As duas FKs que devem existir</b>, e ambas para tabelas de <b>métrica</b>:
    /// a de indexação para a tentativa, a de busca para a execução da task. As
    /// duas são anuláveis — exatamente uma é preenchida por linha, e qual
    /// depende de <c>Purpose</c> (design.md, D9).
    /// </summary>
    [Theory]
    [InlineData("knowledge_indexing_attempts")]
    [InlineData("task_executions")]
    public async Task EmbeddingCalls_HasAForeignKeyTo(string principalTable)
    {
        var foreignKeys = await ScalarAsync<long>(
            "select count(*) from information_schema.table_constraints tc " +
            "join information_schema.constraint_column_usage ccu on tc.constraint_name = ccu.constraint_name " +
            "where tc.constraint_type = 'FOREIGN KEY' and tc.table_name = 'embedding_calls' " +
            $"and ccu.table_name = '{principalTable}';");

        Assert.Equal(1, foreignKeys);
    }

    /// <summary>
    /// D9: a métrica registra o que aconteceu, e o que aconteceu não muda porque
    /// o catálogo mudou depois. Com <c>Restrict</c>, apagar uma base passaria a
    /// falhar; com <c>Cascade</c>, o total de tokens de um período mudaria
    /// retroativamente. A ausência da FK é o que impede os dois.
    /// </summary>
    [Fact]
    public async Task NoEmbeddingMetricsTable_HasAForeignKeyToTheCatalog()
    {
        var foreignKeysToCatalog = await ScalarAsync<long>(
            "select count(*) from information_schema.table_constraints tc " +
            "join information_schema.constraint_column_usage ccu on tc.constraint_name = ccu.constraint_name " +
            "where tc.constraint_type = 'FOREIGN KEY' " +
            "and tc.table_name in ('knowledge_indexing_attempts', 'embedding_calls') " +
            "and ccu.table_name in ('knowledge_bases', 'knowledge_documents', 'agents');");

        Assert.Equal(0, foreignKeysToCatalog);
    }

    /// <summary>
    /// <c>provider_calls</c> NÃO é tocada por esta change (design.md, D1):
    /// <c>TaskId</c> continua obrigatório, e a chave estrangeira continua valendo
    /// para toda linha. O guarda existe porque a etapa 1 registrou o contrário
    /// em D7 dela, e é aqui que a correção fica presa.
    /// </summary>
    [Fact]
    public async Task ProviderCalls_TaskId_RemainsNotNull()
    {
        var actual = await ScalarAsync<string>(
            "select is_nullable from information_schema.columns " +
            "where table_name = 'provider_calls' and column_name = 'TaskId';");

        Assert.Equal("NO", actual);
    }
}
