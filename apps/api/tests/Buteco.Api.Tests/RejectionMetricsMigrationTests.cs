using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Buteco.Api.Tests;

/// <summary>
/// A forma de <c>task_rejections</c>, no molde de
/// <see cref="ExecutionMetricsMigrationTests"/> — e com uma diferença que vale
/// dizer: esta tabela <b>não tem espelho</b> em <c>apps/workers</c>, porque é
/// <c>apps/api</c> quem a escreve e quem a lê. Então este é o único lado, e o par
/// de testes que as outras cinco têm aqui não existe.
///
/// <para>
/// <b>Contêiner próprio</b>, como as outras três classes de migração. É a 41ª
/// classe de contêiner de <c>apps/api</c> — a régua estava registrada como 31 e o
/// real, medido em 25/09/2026 sobre <c>5f2f6f8</c>, era <b>40</b> (a contagem
/// original não incluiu a subpasta <c>Knowledge/</c>). Autorizada pelo dono.
/// </para>
/// </summary>
public class RejectionMetricsMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_rejection_metrics_test")
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
    public async Task Migration_CreatesTheRejectionTable()
    {
        var tables = await ScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema = 'public' " +
            "and table_name = 'task_rejections';");

        Assert.Equal(1, tables);
    }

    /// <summary>
    /// <b>O motivo é TEXTO e é OBRIGATÓRIO</b> — as duas metades carregam decisão.
    /// Texto, nunca ordinal, porque quem lê o banco na agregação lê o valor e não
    /// um índice, e acrescentar um valor ao vocabulário não renumera os outros
    /// (convenção 12). Obrigatório porque a linha só nasce num dos sítios que
    /// decidem a recusa: não existe recusa sem motivo, e é isso que faz a soma dos
    /// motivos fechar com a contagem de recusas da janela.
    ///
    /// <para>
    /// É o inverso do que a convenção 13 pede das colunas de contagem, e de
    /// propósito: ali <c>NOT NULL</c> obrigaria a inventar zero; aqui
    /// <c>NULL</c> permitiria gravar recusa sem causa, que é o defeito que esta
    /// change existe para corrigir.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("TaskId", "text", "NO")]
    [InlineData("AgentId", "uuid", "NO")]
    [InlineData("Reason", "text", "NO")]
    [InlineData("RejectedAt", "timestamp with time zone", "NO")]
    public async Task Columns_HaveTheExpectedTypeAndNullability(string column, string expectedType, string expectedNullable)
    {
        var actual = await ScalarAsync<string>(
            "select data_type || '|' || is_nullable from information_schema.columns " +
            $"where table_name = 'task_rejections' and column_name = '{column}';");

        Assert.Equal($"{expectedType}|{expectedNullable}", actual);
    }

    /// <summary>
    /// <b>NENHUMA chave estrangeira</b> (design.md, D2), e as duas ausências têm
    /// razões diferentes: para <c>a2a_tasks</c>, a linha do protocolo é gravada
    /// pelo <c>ITaskStore</c> do SDK por um caminho cuja ordem contra esta escrita
    /// não é garantida, e a FK transformaria a corrida em falha; para
    /// <c>agents</c>, a cascade que <c>a2a_tasks</c> já tem apagaria a história de
    /// recusas no dia em que existir exclusão de agente.
    ///
    /// <para>
    /// A ausência é <b>exercitada</b> por
    /// <c>SendMessageProviderRejectionTests.RejectionRow_SurvivesAgentDeletion_BecauseItHasNoForeignKey</c>:
    /// este teste prende o schema, aquele prende o comportamento.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RejectionTable_HasNoForeignKeyAtAll()
    {
        var foreignKeys = await ScalarAsync<long>(
            "select count(*) from information_schema.table_constraints " +
            "where constraint_type = 'FOREIGN KEY' and table_name = 'task_rejections';");

        Assert.Equal(0, foreignKeys);
    }

    /// <summary>
    /// Os dois índices são as duas consultas da agregação — janela e recorte por
    /// agente —, e nenhum outro: índice sugerido pela forma não entra sem medição,
    /// como os cinco de <c>task_executions</c> e <c>embedding_calls</c> que também
    /// não entraram.
    /// </summary>
    [Fact]
    public async Task RejectionTable_HasTheTwoIndexesTheAggregationUses()
    {
        var indexed = await ScalarAsync<string>(
            "select string_agg(a.attname, ',' order by a.attname) " +
            "from pg_index i " +
            "join pg_class t on t.oid = i.indrelid " +
            "join pg_attribute a on a.attrelid = t.oid and a.attnum = any(i.indkey) " +
            "where t.relname = 'task_rejections' and not i.indisprimary;");

        Assert.Equal("AgentId,RejectedAt", indexed);
    }
}
