using Buteco.Workers.Agents.Entities;
using Buteco.Workers.ExecutionMetrics.Entities;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Workers.Tests.ExecutionMetrics;

/// <summary>
/// O espelho de <c>apps/workers</c> das três tabelas de métrica — a mesma forma
/// que <c>ExecutionMetricsMigrationTests</c> afirma do lado de <c>apps/api</c>,
/// cuja migração é a que roda em produção. Os dois <c>AppDbContext</c> são
/// sincronizados por disciplina; este par de testes é a verificação.
///
/// <para>
/// <c>IClassFixture</c>, fora da coleção de hosts: não sobe worker nenhum, e uma
/// classe a mais na coleção obrigaria a recalibrar o limiar de carga da suíte.
/// </para>
/// </summary>
public class ExecutionMetricsSchemaMirrorTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
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
    public async Task Migrations_CreateTheThreeMetricsTables()
    {
        var tables = await ScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema = 'public' " +
            "and table_name in ('task_executions', 'provider_calls', 'delegation_outcomes');");

        Assert.Equal(3, tables);
    }

    // Tipo e nulidade das colunas que carregam decisão (D1). Os tokens são o caso
    // da convenção 13: NOT NULL obrigaria a gravar zero onde o provedor não
    // reportou nada.
    [Theory]
    [InlineData("provider_calls", "InputTokens", "bigint", "YES")]
    [InlineData("provider_calls", "OutputTokens", "bigint", "YES")]
    [InlineData("provider_calls", "CachedInputTokens", "bigint", "YES")]
    [InlineData("provider_calls", "Purpose", "text", "NO")]
    [InlineData("provider_calls", "HttpStatus", "integer", "YES")]
    [InlineData("task_executions", "SubmittedAt", "timestamp with time zone", "YES")]
    [InlineData("task_executions", "StartedAt", "timestamp with time zone", "NO")]
    [InlineData("task_executions", "EndedAt", "timestamp with time zone", "YES")]
    [InlineData("task_executions", "TerminalState", "text", "YES")]
    [InlineData("task_executions", "Origin", "text", "NO")]
    [InlineData("delegation_outcomes", "TargetTaskId", "text", "YES")]
    [InlineData("delegation_outcomes", "LastObservedTargetState", "text", "YES")]
    public async Task Columns_HaveTheExpectedTypeAndNullability(
        string table, string column, string expectedType, string expectedNullable)
    {
        var actual = await ScalarAsync<string>(
            $"select data_type || '|' || is_nullable from information_schema.columns " +
            $"where table_name = '{table}' and column_name = '{column}';");

        Assert.Equal($"{expectedType}|{expectedNullable}", actual);
    }

    /// <summary>
    /// O par ESTRUTURAL do guarda de snapshot (D13; convenção 15, quinta forma).
    /// O comportamental — agente troca de modelo, a linha antiga não muda — passa
    /// também com uma FK que ninguém usa para join; o que impede a consulta de
    /// ler o modelo de hoje para o consumo de ontem é não haver relação para
    /// seguir. Afirmado no modelo do EF, que é de onde a migração sai.
    /// </summary>
    [Fact]
    public void EfModel_HasNoForeignKeyFromMetricsToAgents()
    {
        using var dbContext = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

        var foreignKeysToAgents = new[] { typeof(TaskExecution), typeof(ProviderCall), typeof(DelegationOutcome) }
            .SelectMany(type => dbContext.Model.FindEntityType(type)!.GetForeignKeys())
            .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Agent))
            .ToList();

        Assert.Empty(foreignKeysToAgents);
    }
}
