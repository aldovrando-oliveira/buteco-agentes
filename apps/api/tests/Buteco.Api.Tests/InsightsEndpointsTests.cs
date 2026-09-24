using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests;

/// <summary>
/// Guardas da rota de agregação do escopo do sistema.
///
/// <para>
/// <b>Todo cenário roda sobre banco POVOADO, e a precondição é afirmada</b>
/// (<see cref="SeededRowCounts"/>). É a forma da vacuidade: uma agregação sobre
/// tabela vazia devolve zero e nulo, e um guarda exercitado só contra estado
/// limpo fica verde COM e SEM a implementação, porque nunca chega à comparação.
/// </para>
/// </summary>
public class InsightsEndpointsTests : IClassFixture<InsightsEndpointsTests.InsightsFixture>
{
    /// <summary>
    /// Fixture com fuso e regimes FIXADOS, e é o que torna estes guardas
    /// independentes da máquina.
    ///
    /// <para>
    /// <b>Por que um <see cref="FakeTimeProvider"/> com fuso fixo:</b> a checagem
    /// de boot compara o <c>TZ</c> declarado com o fuso que o
    /// <see cref="TimeProvider"/> resolve. Se o declarado fosse o da máquina, o
    /// guarda do balde seria VÁCUO em qualquer máquina em UTC — dia local e dia
    /// UTC coincidiriam, e ele ficaria verde com e sem o defeito. Fixando os dois
    /// lados em <c>America/Sao_Paulo</c>, o balde é sempre -03:00 e o guarda
    /// sempre chega à comparação. O Postgres do contêiner tem a tz database, então
    /// o <c>AT TIME ZONE</c> é real, não simulado.
    /// </para>
    /// </summary>
    public sealed class InsightsFixture : ApiFactoryFixture
    {
        public const string ZoneId = "America/Sao_Paulo";

        public const string ExecutionRegimeStart = "2026-09-01T00:00:00-03:00";

        public const string EmbeddingRegimeStart = "2026-09-10T00:00:00-03:00";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TZ"] = ZoneId,
                    ["Metrics:Regimes:execution"] = ExecutionRegimeStart,
                    ["Metrics:Regimes:embedding"] = EmbeddingRegimeStart,
                }));

            builder.ConfigureServices(services =>
            {
                var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
                timeProvider.SetLocalTimeZone(
                    TimeZoneInfo.CreateCustomTimeZone(ZoneId, TimeSpan.FromHours(-3), "-03:00", "-03:00"));

                services.AddSingleton<TimeProvider>(timeProvider);
            });
        }
    }

    private readonly InsightsFixture _fixture;

    /// <summary>
    /// O instante que decide o guarda do balde: <b>2026-09-15T02:30Z</b> é
    /// <b>segunda-feira 14/09 às 23:30</b> em <c>America/Sao_Paulo</c> — outro dia
    /// <i>e</i> outro dia da semana. Em UTC é terça 15/09.
    /// </summary>
    private const string NightInstant = "2026-09-15T02:30:00Z";

    private static readonly DateOnly ExpectedLocalDay = new(2026, 9, 14);

    private static readonly DateOnly UtcDay = new(2026, 9, 15);

    private const DayOfWeek ExpectedLocalWeekday = DayOfWeek.Monday;

    private const DayOfWeek UtcWeekday = DayOfWeek.Tuesday;

    /// <summary>
    /// O dia LOCAL do instante fixado no <c>FakeTimeProvider</c>
    /// (<c>2026-09-23T12:00:00Z</c> é 23/09 às 09:00 em <c>-03:00</c>). É o
    /// limite superior da série, e é o que torna o guarda do dia futuro
    /// determinístico.
    /// </summary>
    private static readonly DateOnly QueryInstantLocalDay = new(2026, 9, 23);

    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid TargetAgentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public InsightsEndpointsTests(InsightsFixture fixture)
    {
        _fixture = fixture;
        SeedAsync().GetAwaiter().GetResult();
    }

    // ------------------------------------------------------------- Janela

    [Fact]
    public async Task Window_WithMissingAndMalformedBounds_ReturnsOneValidationResponseForBoth()
    {
        var response = await _fixture.CreateClient().GetAsync("/insights/system?to=nao-e-uma-data");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);

        Assert.Contains("from", problem.Keys);
        Assert.Contains("to", problem.Keys);
    }

    [Fact]
    public async Task Window_WithInvertedPeriod_IsRejectedUnderTheFinalBound()
    {
        var response = await _fixture.CreateClient()
            .GetAsync("/insights/system?from=2026-09-20T00:00:00Z&to=2026-09-10T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);

        Assert.Contains("to", problem.Keys);
        Assert.DoesNotContain("from", problem.Keys);
    }

    /// <summary>
    /// O guarda do <c>AdjustToUniversal</c>. <b>Ele só é alcançável depois de
    /// <c>apps/api</c> receber <c>TZ</c>:</b> num processo em UTC o deslocamento
    /// local já é zero e o defeito não se manifesta — o guarda ficaria verde dos
    /// dois lados, provando nada. Aqui o fuso é -03:00 por construção.
    /// </summary>
    [Fact]
    public async Task Window_WithNonZeroOffsetBound_IsAcceptedAndNotAnInternalError()
    {
        var response = await _fixture.CreateClient()
            .GetAsync("/insights/system?from=2026-09-01T00:00:00-03:00&to=2026-09-30T23:59:59-03:00");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Window_BoundsAreInclusiveOnBothEnds()
    {
        // A janela começa e termina EXATAMENTE no instante da task noturna.
        var insights = await GetAsync(NightInstant, NightInstant);

        Assert.Equal(1, insights.GetProperty("volume").GetProperty("executedTaskCount").GetInt32());
    }

    // -------------------------------------------------------- Balde local

    /// <summary>
    /// <b>O guarda central desta change.</b> Reprova contra balde em UTC nas duas
    /// pontas: o dia e o dia da semana.
    /// </summary>
    /// <summary>
    /// A ASSERÇÃO MUDOU DE FORMA, e a mudança é consequência direta da série
    /// passar a cobrir todo dia medido.
    ///
    /// <para>
    /// Antes, o guarda afirmava a AUSÊNCIA do dia UTC. Ele discriminava porque a
    /// série só tinha dias com ocorrência: se o balde saísse em UTC, 15/09
    /// aparecia; se saísse local, não. Com a série cobrindo o domínio de dias,
    /// <b>15/09 aparece nos dois casos</b> — e a ausência deixa de separar nada.
    /// </para>
    ///
    /// <para>
    /// A forma nova é ESTRITAMENTE MAIS FORTE: afirma <b>onde a task caiu</b>.
    /// Balde em UTC põe a contagem em 15/09 e zera 14/09, e as duas asserções
    /// reprovam. É o mesmo defeito guardado por um par que continua exprimível
    /// depois da mudança de domínio.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DailyBucket_UsesTheLocalDay_NotTheUtcDay()
    {
        var insights = await GetAsync("2026-09-14T00:00:00-03:00", "2026-09-15T23:59:59-03:00");

        var series = DailySeries(insights);

        Assert.Contains(ExpectedLocalDay, series.Keys);
        Assert.Contains(UtcDay, series.Keys);

        // A task noturna caiu no dia LOCAL. O dia UTC existe na série — porque
        // foi medido — e está ZERADO, que é outra afirmação.
        Assert.Equal(1, series[ExpectedLocalDay].TaskCount);
        Assert.Equal(0, series[UtcDay].TaskCount);
    }

    /// <inheritdoc cref="DailyBucket_UsesTheLocalDay_NotTheUtcDay"/>
    [Fact]
    public async Task Weekday_FollowsTheLocalDay_NotTheUtcDay()
    {
        var insights = await GetAsync("2026-09-14T00:00:00-03:00", "2026-09-15T23:59:59-03:00");

        var weekdays = Weekdays(insights);

        Assert.Contains(ExpectedLocalWeekday, weekdays.Keys);
        Assert.Contains(UtcWeekday, weekdays.Keys);

        Assert.Equal(1, weekdays[ExpectedLocalWeekday]);
        Assert.Equal(0, weekdays[UtcWeekday]);
    }

    // ---------------------------------------------------------------- Nulo

    /// <summary>
    /// O guarda NEGATIVO: a asserção é a ausência do zero. A positiva
    /// ("é nulo ou zero") passaria igual com o comportamento certo e com o
    /// errado, que é o que a convenção 13 existe para impedir.
    /// </summary>
    [Fact]
    public async Task UnreportedTokens_ArriveAsNull_NeverAsZero()
    {
        // Janela que contém SÓ a task cuja chamada não reportou token nenhum.
        var insights = await GetAsync("2026-09-14T00:00:00-03:00", "2026-09-15T00:00:00-03:00");

        var conversation = insights.GetProperty("tokens").GetProperty("conversation");
        var input = conversation.GetProperty("inputTokens");

        Assert.Equal(JsonValueKind.Null, input.ValueKind);

        // E a precondição que impede o guarda de ser vácuo: a chamada existe.
        Assert.True(insights.GetProperty("volume").GetProperty("executedTaskCount").GetInt32() > 0);
    }

    [Fact]
    public async Task MeasuredAndEmptyPeriod_ArrivesAsZero_NotAsNull()
    {
        // Dentro do regime de execução, e sem nenhuma task.
        var insights = await GetAsync("2026-09-02T00:00:00-03:00", "2026-09-03T00:00:00-03:00");

        var executed = insights.GetProperty("volume").GetProperty("executedTaskCount");

        Assert.Equal(JsonValueKind.Number, executed.ValueKind);
        Assert.Equal(0, executed.GetInt32());
    }

    // -------------------------------------------------------------- Regime

    [Fact]
    public async Task Regimes_ArriveSeparately_WithTheirOwnStarts()
    {
        var insights = await GetAsync("2026-09-01T00:00:00-03:00", "2026-09-30T00:00:00-03:00");

        var regimes = insights.GetProperty("regimes");

        var execution = DateTimeOffset.Parse(regimes.GetProperty("execution").GetString()!);
        var embedding = DateTimeOffset.Parse(regimes.GetProperty("embedding").GetString()!);

        Assert.NotEqual(execution, embedding);
        Assert.Equal(DateTimeOffset.Parse(InsightsFixture.ExecutionRegimeStart), execution);
        Assert.Equal(DateTimeOffset.Parse(InsightsFixture.EmbeddingRegimeStart), embedding);
    }

    /// <summary>
    /// O par do "não medido" × "medido e vazio": pedir uma janela que começa
    /// ANTES do regime não pode produzir pontos de série com <c>0</c> nos dias
    /// anteriores — eles simplesmente não existem.
    ///
    /// <para>
    /// <b>A PRECONDIÇÃO É O QUE FAZ ESTE GUARDA DISCRIMINAR</b>, e ela foi
    /// acrescentada pela change <c>serie-diaria-dia-medido-vazio</c>. Sem ela o
    /// guarda passava por VACUIDADE: <c>Assert.All</c> sobre lista vazia é
    /// verde, e a implementação que ele deveria prender era precisamente uma que
    /// não emitia dia nenhum. Ele ficou verde contra o defeito durante duas
    /// changes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DaysBeforeTheRegime_AreOmitted_NeverEmittedAsZero()
    {
        var insights = await GetAsync("2026-08-01T00:00:00-03:00", "2026-09-30T23:59:59-03:00");

        var days = DailySeries(insights).Keys.ToList();

        var regimeStart = DateOnly.FromDateTime(DateTimeOffset.Parse(InsightsFixture.ExecutionRegimeStart).Date);

        // PRECONDIÇÃO: a série cobre o regime inteiro até o instante da consulta
        // — 01/09 a 23/09. Só depois de afirmar que há dias é que "nenhum deles
        // é anterior ao regime" diz alguma coisa.
        Assert.Equal(23, days.Count);
        Assert.Equal(regimeStart, days.Min());
        Assert.Equal(QueryInstantLocalDay, days.Max());

        Assert.All(days, day => Assert.True(day >= regimeStart, $"{day} é anterior ao início do regime {regimeStart}"));
    }

    // ------------------------------------------- Dia medido e sem ocorrência

    /// <summary>
    /// <b>O par que faltava.</b> A spec exige as duas metades — omitir o dia
    /// anterior ao regime E emitir <c>0</c> para o dia medido e vazio —, e só a
    /// negativa tinha guarda. O seed já continha o caso desde a change que criou
    /// a rota: 14/09 e 16/09 com execução, <b>15/09 vazio</b>, regime em 01/09.
    /// </summary>
    [Fact]
    public async Task MeasuredAndEmptyDay_AppearsInTheSeries_WithZero()
    {
        var insights = await GetAsync("2026-09-14T00:00:00-03:00", "2026-09-16T23:59:59-03:00");

        var series = DailySeries(insights);

        // Os três dias, sem buraco no meio.
        Assert.Equal([new(2026, 9, 14), new(2026, 9, 15), new(2026, 9, 16)], series.Keys.ToList());

        Assert.Equal(1, series[new(2026, 9, 14)].TaskCount);
        Assert.Equal(0, series[new(2026, 9, 15)].TaskCount);
        Assert.Equal(3, series[new(2026, 9, 16)].TaskCount);
    }

    /// <summary>
    /// "Foram zero tasks" e "não há token a relatar" são afirmações DIFERENTES, e
    /// o <c>0</c> de uma não pode vazar para a outra. O par vive na mesma linha.
    /// </summary>
    [Fact]
    public async Task MeasuredAndEmptyDay_HasNullTokenCount_NotZero()
    {
        var insights = await GetAsync("2026-09-14T00:00:00-03:00", "2026-09-16T23:59:59-03:00");

        var emptyDay = DailySeries(insights)[new DateOnly(2026, 9, 15)];

        Assert.Equal(0, emptyDay.TaskCount);

        // A asserção NEGATIVA: o contador de tokens não é 0.
        Assert.Equal(JsonValueKind.Null, emptyDay.TokenCount.ValueKind);
    }

    /// <summary>
    /// O simétrico do dia anterior ao regime, e o defeito que a própria correção
    /// criaria se o limite superior fosse o <c>to</c> pedido: emitir <c>0</c>
    /// para dia que ainda não aconteceu afirma medição sobre o futuro
    /// (design.md, D3). O instante da consulta é o do <c>FakeTimeProvider</c>.
    /// </summary>
    [Fact]
    public async Task DaysAfterTheQueryInstant_AreNotEmittedAsZero()
    {
        var insights = await GetAsync("2026-09-22T00:00:00-03:00", "2026-09-30T23:59:59-03:00");

        var days = DailySeries(insights).Keys.ToList();

        Assert.Equal([new(2026, 9, 22), QueryInstantLocalDay], days);
        Assert.DoesNotContain(days, day => day > QueryInstantLocalDay);
    }

    // ------------------------------------------------------- Dia da semana

    /// <summary>
    /// A armadilha da forma (design.md, D4): com o <c>left join</c> ao domínio de
    /// dias, <c>count(*)</c> devolve <b>1</b> no dia sem correspondência, e todo
    /// dia da semana vazio chegaria com <c>1</c> — número pequeno, positivo, sem
    /// sintoma. Por isso a asserção é contra <c>0</c>, e não contra "algum
    /// número".
    /// </summary>
    [Fact]
    public async Task WeekdayCoveredAndEmpty_ArrivesAsZero()
    {
        var insights = await GetAsync("2026-09-14T00:00:00-03:00", "2026-09-16T23:59:59-03:00");

        var weekdays = Weekdays(insights);

        // 14/09 é segunda, 15/09 terça, 16/09 quarta. A terça foi medida e não
        // teve nenhuma task.
        Assert.Equal(0, weekdays[DayOfWeek.Tuesday]);
        Assert.Equal(1, weekdays[DayOfWeek.Monday]);
        Assert.Equal(3, weekdays[DayOfWeek.Wednesday]);
    }

    /// <summary>
    /// O par do de cima: dia da semana que NÃO ocorre na faixa medida nunca foi
    /// medido, e emitir <c>0</c> para ele afirmaria medição que não houve.
    /// </summary>
    [Fact]
    public async Task WeekdayNotCoveredByTheMeasuredRange_IsOmitted()
    {
        var insights = await GetAsync("2026-09-14T00:00:00-03:00", "2026-09-15T23:59:59-03:00");

        var weekdays = Weekdays(insights);

        // A faixa medida tem dois dias: segunda e terça. Os outros cinco não
        // ocorrem nela.
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Tuesday], weekdays.Keys.OrderBy(day => day).ToList());
    }

    /// <summary>
    /// <b>A regressão que a correção introduziria.</b> Com o dia da semana
    /// passando a emitir <c>0</c>, uma faixa medida sem nenhuma ocorrência
    /// produz contagens todas iguais a zero — e o <c>MaxBy</c> elegeria DOMINGO
    /// como pico de um período em que nada aconteceu. O comentário do handler já
    /// proibia isso em palavras; este guarda o prende (design.md, D5).
    /// </summary>
    [Fact]
    public async Task PeriodMeasuredWithoutOccurrences_HasNoPeakWeekday()
    {
        // Dentro do regime de execução, e sem nenhuma task.
        var insights = await GetAsync("2026-09-02T00:00:00-03:00", "2026-09-03T00:00:00-03:00");

        var temporal = insights.GetProperty("temporal");

        // PRECONDIÇÃO: há dias da semana na resposta, todos zerados. Sem ela, o
        // pico nulo sairia de uma lista vazia e o guarda não discriminaria.
        var weekdays = Weekdays(insights);
        Assert.NotEmpty(weekdays);
        Assert.All(weekdays.Values, count => Assert.Equal(0, count));

        Assert.Equal(JsonValueKind.Null, temporal.GetProperty("peakWeekday").ValueKind);
    }

    // ------------------------------------------------------------------ M32

    [Fact]
    public async Task NonTerminal_ReportsBothPopulationsSeparately()
    {
        var insights = await GetAsync("2026-09-01T00:00:00-03:00", "2026-09-30T00:00:00-03:00");

        var nonTerminal = insights.GetProperty("errors").GetProperty("nonTerminal");

        // Execução aberta: a task consumida e ainda rodando.
        Assert.Equal(1, nonTerminal.GetProperty("openExecutionCount").GetInt32());

        // Nunca consumida: só existe em a2a_tasks, sem linha de execução.
        Assert.Equal(1, nonTerminal.GetProperty("neverConsumedCount").GetInt32());
    }

    [Fact]
    public async Task NonTerminal_NeverReportsATerminalTask_HoweverOld()
    {
        var insights = await GetAsync("2026-09-01T00:00:00-03:00", "2026-09-30T00:00:00-03:00");

        var nonTerminal = insights.GetProperty("errors").GetProperty("nonTerminal");

        // Há uma a2a_task Completed de 2020 semeada, também sem linha de
        // execução. Se o conjunto de estados vazasse, ela entraria aqui.
        Assert.Equal(1, nonTerminal.GetProperty("neverConsumedCount").GetInt32());

        var states = nonTerminal.GetProperty("observedStates").EnumerateArray()
            .Select(state => state.GetString())
            .ToList();

        // O MESMO conjunto do detector de apps/workers — é a coexistência que
        // prova que as duas fontes medem o mesmo número.
        Assert.Equal(["Submitted", "Working"], states);
    }

    // ------------------------------------------------------- Parcialidades

    [Fact]
    public async Task Rejections_AreNotPresentedAsIfTheCountWereComplete()
    {
        var insights = await GetAsync("2026-09-01T00:00:00-03:00", "2026-09-30T00:00:00-03:00");

        var caveats = insights.GetProperty("errors").GetProperty("caveats")
            .EnumerateArray().Select(caveat => caveat.GetString()).ToList();

        Assert.Contains("rejections-missing-from-executions", caveats);
        Assert.Contains("rejection-reason-not-collected", caveats);
    }

    /// <summary>
    /// O carimbo de submissão ausente produz INDEFINIDO, não zero: a execução
    /// reentregue fica fora da amostra em vez de entrar com duração zero.
    /// </summary>
    [Fact]
    public async Task ExecutionWithoutSubmittedAt_IsExcludedFromTheSample_NotCountedAsZero()
    {
        var insights = await GetAsync("2026-09-16T00:00:00-03:00", "2026-09-17T00:00:00-03:00");

        var performance = insights.GetProperty("performance");
        var queueTime = performance.GetProperty("queueTime");

        // Três execuções na janela; só duas têm carimbo de submissão.
        Assert.Equal(3, insights.GetProperty("volume").GetProperty("executedTaskCount").GetInt32());
        Assert.Equal(2, queueTime.GetProperty("sampleCount").GetInt32());

        var caveats = performance.GetProperty("caveats").EnumerateArray()
            .Select(caveat => caveat.GetString()).ToList();

        Assert.Contains("submitted-at-missing-on-redelivery", caveats);
        Assert.Contains("residual-is-not-only-tools", caveats);
    }

    // ------------------------------------------------------- Autenticação

    [Fact]
    public async Task Route_WithoutToken_Returns401()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("/insights/system?from=2026-09-01T00:00:00Z&to=2026-09-30T00:00:00Z");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------ Apoio

    private async Task<JsonElement> GetAsync(string from, string to)
    {
        var response = await _fixture.CreateClient()
            .GetAsync($"/insights/system?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Um ponto da série, com o contador de tokens CRU — e não convertido —
    /// porque o guarda precisa distinguir <c>null</c> de <c>0</c>, e qualquer
    /// conversão para <c>long?</c> aqui reintroduziria no teste o colapso que o
    /// teste existe para proibir.
    /// </summary>
    private sealed record DailyPoint(int TaskCount, JsonElement TokenCount);

    private static SortedDictionary<DateOnly, DailyPoint> DailySeries(JsonElement insights) =>
        new(insights.GetProperty("temporal").GetProperty("dailySeries")
            .EnumerateArray()
            .ToDictionary(
                point => DateOnly.Parse(point.GetProperty("day").GetString()!),
                point => new DailyPoint(
                    point.GetProperty("taskCount").GetInt32(),
                    point.GetProperty("tokenCount"))));

    private static Dictionary<DayOfWeek, int> Weekdays(JsonElement insights) =>
        insights.GetProperty("temporal").GetProperty("byWeekday")
            .EnumerateArray()
            .ToDictionary(
                point => (DayOfWeek)point.GetProperty("weekday").GetInt32(),
                point => point.GetProperty("taskCount").GetInt32());

    private static async Task<Dictionary<string, string[]>> ReadProblemAsync(HttpResponseMessage response)
    {
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = document.GetProperty("errors");

        return errors.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    /// <summary>
    /// A precondição que impede a vacuidade: o guarda conta as linhas semeadas
    /// ANTES de asserir qualquer agregação. Sem isto, todo cenário deste arquivo
    /// ficaria verde sobre banco vazio.
    /// </summary>
    private sealed record SeededRowCounts(int Executions, int ProviderCalls, int A2ATasks);

    private async Task SeedAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await dbContext.Database.SqlQuery<int>(
                $"""select count(*)::int as "Value" from task_executions""").SingleAsync() > 0)
        {
            return;
        }

        await dbContext.Database.ExecuteSqlAsync($"""
            insert into agents ("Id", "Name", "Instructions", "CreatedAt", "UpdatedAt", "IsActive", skills)
            values ({AgentId}, 'Agente de insights', '', now(), now(), true, '[]'::jsonb),
                   ({TargetAgentId}, 'Agente alvo', '', now(), now(), true, '[]'::jsonb);
            """);

        // 1) A task NOTURNA — 2026-09-15T02:30Z é 14/09 23:30 em -03:00.
        //    A chamada ao provedor NÃO reporta token: é a fonte do guarda de nulo.
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into task_executions
                ("TaskId", "AgentId", "ContextId", "Provider", "Model", "Origin",
                 "DelegationDepth", "SubmittedAt", "StartedAt", "EndedAt", "TerminalState")
            values ('task-night', {AgentId}, 'ctx-1', 'openai', 'modelo-a', 'External',
                    0, timestamptz '2026-09-15T02:29:00Z', timestamptz '2026-09-15T02:30:00Z',
                    timestamptz '2026-09-15T02:31:00Z', 'Completed');
            """);

        await dbContext.Database.ExecuteSqlAsync($"""
            insert into provider_calls
                ("Id", "TaskId", "Provider", "Model", "Purpose", "DurationMs",
                 "InputTokens", "OutputTokens", "CachedInputTokens", "Failed")
            values (gen_random_uuid(), 'task-night', 'openai', 'modelo-a', 'Turn', 120.0,
                    null, null, null, false);
            """);

        // 2) Task com token reportado, fora da janela do guarda de nulo.
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into task_executions
                ("TaskId", "AgentId", "ContextId", "Provider", "Model", "Origin",
                 "DelegationDepth", "SubmittedAt", "StartedAt", "EndedAt", "TerminalState")
            values ('task-tokens', {AgentId}, 'ctx-2', 'openai', 'modelo-a', 'External',
                    0, timestamptz '2026-09-16T11:59:00Z', timestamptz '2026-09-16T12:00:00Z',
                    timestamptz '2026-09-16T12:00:30Z', 'Completed');

            insert into provider_calls
                ("Id", "TaskId", "Provider", "Model", "Purpose", "DurationMs",
                 "InputTokens", "OutputTokens", "CachedInputTokens", "Failed")
            values (gen_random_uuid(), 'task-tokens', 'openai', 'modelo-a', 'Turn', 300.0,
                    100, 50, 10, false);
            """);

        // 3) REENTREGA — sem carimbo de submissão. Fica fora da amostra de
        //    duração e de tempo de fila, e é o que o guarda da parcialidade lê.
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into task_executions
                ("TaskId", "AgentId", "ContextId", "Provider", "Model", "Origin",
                 "DelegationDepth", "SubmittedAt", "StartedAt", "EndedAt", "TerminalState")
            values ('task-redelivered', {AgentId}, 'ctx-3', 'openai', 'modelo-a', 'Delegation',
                    1, null, timestamptz '2026-09-16T13:00:00Z',
                    timestamptz '2026-09-16T13:00:10Z', 'Failed');
            """);

        // 4) EXECUÇÃO ABERTA — primeira população de M32.
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into task_executions
                ("TaskId", "AgentId", "ContextId", "Provider", "Model", "Origin",
                 "DelegationDepth", "SubmittedAt", "StartedAt", "EndedAt", "TerminalState")
            values ('task-open', {AgentId}, 'ctx-4', 'openai', 'modelo-a', 'External',
                    0, timestamptz '2026-09-16T13:59:00Z', timestamptz '2026-09-16T14:00:00Z',
                    null, null);
            """);

        await dbContext.Database.ExecuteSqlAsync($"""
            insert into delegation_outcomes
                ("Id", "SourceTaskId", "SourceAgentId", "TargetAgentId", "TargetTaskId",
                 "Outcome", "LastObservedTargetState", "LastObservedAt", "SuccessfulReadCount", "DurationMs")
            values (gen_random_uuid(), 'task-tokens', {AgentId}, {TargetAgentId}, null,
                    'Expired', 'Submitted', timestamptz '2026-09-16T12:00:20Z', 2, 5000.0);
            """);

        // 5) a2a_tasks: a NUNCA CONSUMIDA (segunda população de M32) e uma
        //    terminal antiga, que nenhum conjunto de estados pode deixar vazar.
        const string emptyPayload = "{}";

        await dbContext.Database.ExecuteSqlAsync($"""
            insert into a2a_tasks (task_id, agent_id, context_id, state, status_timestamp, payload)
            values ('never-consumed', {AgentId}, 'ctx-5', 'Submitted',
                    timestamptz '2026-09-16T15:00:00Z', {emptyPayload}::jsonb),
                   ('terminal-old', {AgentId}, 'ctx-6', 'Completed',
                    timestamptz '2020-01-01T00:00:00Z', {emptyPayload}::jsonb);
            """);

        var counts = await dbContext.Database.SqlQuery<SeededRowCounts>($"""
            select (select count(*) from task_executions)::int as "Executions",
                   (select count(*) from provider_calls)::int as "ProviderCalls",
                   (select count(*) from a2a_tasks)::int as "A2ATasks"
            """).SingleAsync();

        // A asserção da precondição, e não só a semeadura: é ela que impede
        // todo guarda deste arquivo de ficar verde sobre banco vazio.
        Assert.Equal(new SeededRowCounts(4, 2, 2), counts);
    }
}
