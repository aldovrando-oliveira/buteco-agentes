using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Buteco.Api.Tests;

/// <summary>
/// A migração <c>AddExecutionMetrics</c> é a que roda em produção — o espelho de
/// <c>apps/workers</c> só roda contra Testcontainers. Por isso a forma do schema
/// é afirmada aqui, e de novo do lado de lá (<c>ExecutionMetricsSchemaMirrorTests</c>):
/// os dois <c>AppDbContext</c> são sincronizados por disciplina, e é o par de
/// testes que transforma a disciplina em verificação.
/// </summary>
public class ExecutionMetricsMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_execution_metrics_test")
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
    public async Task Migration_CreatesTheThreeMetricsTables()
    {
        var tables = await ScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema = 'public' " +
            "and table_name in ('task_executions', 'provider_calls', 'delegation_outcomes');");

        Assert.Equal(3, tables);
    }

    // Só as colunas cuja nulidade CARREGA DECISÃO. Os tokens são o caso da
    // convenção 13 — uma coluna NOT NULL obrigaria a gravar zero onde o provedor
    // não reportou nada. As demais anuláveis são as que dizem "não se sabe" ou
    // "ainda não aconteceu" (D1, D4, D5), e virar NOT NULL obrigaria a inventar
    // valor.
    [Theory]
    [InlineData("provider_calls", "InputTokens", "bigint")]
    [InlineData("provider_calls", "OutputTokens", "bigint")]
    [InlineData("provider_calls", "CachedInputTokens", "bigint")]
    [InlineData("task_executions", "SubmittedAt", "timestamp with time zone")]
    [InlineData("task_executions", "EndedAt", "timestamp with time zone")]
    [InlineData("task_executions", "TerminalState", "text")]
    [InlineData("delegation_outcomes", "TargetTaskId", "text")]
    [InlineData("delegation_outcomes", "LastObservedTargetState", "text")]
    public async Task DecisionColumns_AreNullable_WithTheExpectedType(string table, string column, string expectedType)
    {
        var actual = await ScalarAsync<string>(
            $"select data_type || '|' || is_nullable from information_schema.columns " +
            $"where table_name = '{table}' and column_name = '{column}';");

        Assert.Equal($"{expectedType}|YES", actual);
    }

    // D13: provedor e modelo são snapshot na linha. Uma FK para `agents`
    // convidaria a consulta a fazer join e ler o modelo de HOJE para o consumo
    // de ontem — e é a ausência dela que este teste prende.
    [Fact]
    public async Task NoMetricsTable_HasAForeignKeyToAgents()
    {
        var foreignKeysToAgents = await ScalarAsync<long>(
            "select count(*) from information_schema.table_constraints tc " +
            "join information_schema.constraint_column_usage ccu on tc.constraint_name = ccu.constraint_name " +
            "where tc.constraint_type = 'FOREIGN KEY' " +
            "and tc.table_name in ('task_executions', 'provider_calls', 'delegation_outcomes') " +
            "and ccu.table_name = 'agents';");

        Assert.Equal(0, foreignKeysToAgents);
    }
}
