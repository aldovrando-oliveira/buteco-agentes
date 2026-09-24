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
/// Guardas da rota de agregação do escopo de um agente.
///
/// <para>
/// <b>Todo cenário roda sobre banco POVOADO, e a precondição é afirmada</b>
/// (<see cref="SeededRowCounts"/>). É a forma da vacuidade: uma agregação sobre
/// tabela vazia devolve zero e nulo, e um guarda exercitado só contra estado
/// limpo fica verde COM e SEM a implementação, porque nunca chega à comparação.
/// </para>
///
/// <para>
/// <b>31ª classe de teste de <c>apps/api</c> que sobe contêiner</b> — a régua de
/// contenção foi recalibrada na própria change que a acrescentou (convenção 22),
/// e não deixada para a seguinte descobrir. O registro diz também PARA QUE o
/// número serve, porque referência que ninguém consulta não é régua: ver o
/// <c>02</c>, e a régua gêmea de <c>WorkerHostCollection</c> em
/// <c>apps/workers</c>, que é a mesma família mantida à mão nos dois apps.
/// </para>
/// </summary>
public class AgentInsightsEndpointsTests : IClassFixture<AgentInsightsEndpointsTests.AgentInsightsFixture>
{
    /// <summary>
    /// Fixture PRÓPRIO, e não o de <c>InsightsEndpointsTests</c>.
    ///
    /// <para>
    /// <b>Não é escolha de estilo.</b> O contêiner é campo de INSTÂNCIA de
    /// <c>ApiFactoryFixture</c>, e o xUnit cria uma instância de
    /// <c>IClassFixture&lt;T&gt;</c> por classe de teste — reusar o tipo daria um
    /// segundo contêiner de qualquer modo, sem compartilhar banco. E a semeadura
    /// daquela classe tem guarda de idempotência que retorna cedo se já houver
    /// linha: duas classes semeando a mesma base com esse guarda produziriam
    /// resultado DEPENDENTE DA ORDEM de execução, que é a quinta forma da
    /// convenção 15 — verde ou vermelho conforme o plano do runner.
    /// </para>
    ///
    /// <para>
    /// Fuso e regimes FIXADOS pelo mesmo motivo do gêmeo: se o declarado fosse o
    /// da máquina, o guarda do balde seria VÁCUO em qualquer máquina em UTC.
    /// </para>
    /// </summary>
    public sealed class AgentInsightsFixture : ApiFactoryFixture
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

    private readonly AgentInsightsFixture _fixture;

    /// <summary>
    /// O instante que decide o guarda do balde: <b>2026-09-15T02:30Z</b> é
    /// <b>segunda-feira 14/09 às 23:30</b> em <c>America/Sao_Paulo</c> — outro
    /// dia <i>e</i> outro dia da semana. Em UTC é terça 15/09.
    /// </summary>
    private const string NightInstant = "2026-09-15T02:30:00Z";

    private static readonly DateOnly ExpectedLocalDay = new(2026, 9, 14);

    private static readonly DateOnly UtcDay = new(2026, 9, 15);

    private const DayOfWeek ExpectedLocalWeekday = DayOfWeek.Monday;

    private const DayOfWeek UtcWeekday = DayOfWeek.Tuesday;

    private const string WindowFrom = "2026-09-01T00:00:00Z";

    private const string WindowTo = "2026-09-30T23:59:59Z";

    /// <summary>
    /// A janela dos guardas do dia medido e vazio: 14/09 00:00 a 17/09 23:59 em
    /// <c>-03:00</c>. O agente consultado tem execução em <b>14/09</b> e em
    /// <b>17/09</b>; <b>15/09</b> não teve execução nenhuma no sistema e
    /// <b>16/09</b> teve — de OUTROS agentes. Os dois são dias medidos.
    /// </summary>
    private const string GapWindowFrom = "2026-09-14T03:00:00Z";

    /// <inheritdoc cref="GapWindowFrom"/>
    private const string GapWindowTo = "2026-09-18T02:59:59Z";

    /// <summary>
    /// O dia LOCAL do instante fixado no <c>FakeTimeProvider</c>
    /// (<c>2026-09-23T12:00:00Z</c> é 23/09 às 09:00 em <c>-03:00</c>) — o limite
    /// superior da série.
    /// </summary>
    private static readonly DateOnly QueryInstantLocalDay = new(2026, 9, 23);

    /// <summary>Cenário 3 do protótipo: delega <b>e</b> é delegado.</summary>
    private static readonly Guid BothAgentId = Guid.Parse("b0b0b0b0-0000-0000-0000-000000000001");

    /// <summary>Cenário 1 do protótipo: <b>só delega</b>.</summary>
    private static readonly Guid OnlyDelegatesAgentId = Guid.Parse("d0d0d0d0-0000-0000-0000-000000000002");

    /// <summary>Cenário 2 do protótipo: <b>só é delegado</b>.</summary>
    private static readonly Guid OnlyTriggeredAgentId = Guid.Parse("70707070-0000-0000-0000-000000000003");

    /// <summary>Existe e não tem nenhum dado — o par que torna o <c>404</c> discriminante.</summary>
    private static readonly Guid EmptyAgentId = Guid.Parse("e0e0e0e0-0000-0000-0000-000000000004");

    /// <summary>Existe e está INATIVO — inatividade não é ausência de sujeito.</summary>
    private static readonly Guid InactiveAgentId = Guid.Parse("1a1a1a1a-0000-0000-0000-000000000005");

    private static readonly Guid KnowledgeBaseId = Guid.Parse("cbcbcbcb-0000-0000-0000-000000000006");

    public AgentInsightsEndpointsTests(AgentInsightsFixture fixture)
    {
        _fixture = fixture;
        SeedAsync().GetAwaiter().GetResult();
    }

    // ------------------------------------------------------------- A rota

    [Fact]
    public async Task Route_ForExistingAgent_ReturnsTheAggregate()
    {
        var body = await GetAsync(BothAgentId);

        Assert.Equal(BothAgentId, body.GetProperty("agentId").GetGuid());
        Assert.Equal(AgentInsightsFixture.ZoneId, body.GetProperty("window").GetProperty("timeZone").GetString());

        // Objeto agregado, nunca linha bruta: não há coleção de entidades no
        // corpo. As chaves do escopo do SISTEMA que não existem aqui também não
        // podem ter vazado.
        Assert.False(body.GetProperty("tokens").TryGetProperty("byAgent", out _));
        Assert.False(body.GetProperty("errors").TryGetProperty("byAgent", out _));
    }

    [Fact]
    public async Task Aggregate_ExcludesWhatBelongsToAnotherAgent()
    {
        var both = await GetAsync(BothAgentId);
        var onlyDelegates = await GetAsync(OnlyDelegatesAgentId);

        // Both tem 2 execuções na janela; OnlyDelegates tem 1. Se o recorte
        // vazasse, os dois veriam o total.
        Assert.Equal(2, both.GetProperty("volume").GetProperty("executedTaskCount").GetInt32());
        Assert.Equal(1, onlyDelegates.GetProperty("volume").GetProperty("executedTaskCount").GetInt32());

        // E o modelo do agente vizinho não aparece na distribuição de Both.
        var models = both.GetProperty("tokens").GetProperty("byModel").EnumerateArray()
            .Select(row => row.GetProperty("model").GetString())
            .ToList();
        Assert.DoesNotContain("modelo-do-vizinho", models);
    }

    // ------------------------------------------------- Existência: 404 e o par

    [Fact]
    public async Task UnknownAgent_Returns404_NotAnEmptyAggregate()
    {
        var response = await _fixture.CreateClient()
            .GetAsync($"/insights/agents/{Guid.NewGuid()}?from={WindowFrom}&to={WindowTo}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExistingAgentWithoutData_Returns200WithMeasuredZero_NotA404()
    {
        // O PAR que torna o 404 discriminante: sem ele, um 404 para tudo
        // passaria no guarda de cima.
        var body = await GetAsync(EmptyAgentId);

        Assert.Equal(0, body.GetProperty("volume").GetProperty("executedTaskCount").GetInt32());

        // A SÉRIE NÃO É VAZIA, E ISSO É O REQUISITO — não uma concessão.
        //
        // A asserção aqui era `Assert.Empty`, e ela codificava o defeito que a
        // change serie-diaria-dia-medido-vazio corrigiu: agente sem dado
        // devolvia série vazia, indistinguível de agente cujo período nunca foi
        // medido. Os dias DA JANELA FORAM MEDIDOS pelo sistema; quem deu zero
        // foi a participação deste agente, e cada dia diz isso com 0.
        var series = DailySeries(body);

        Assert.NotEmpty(series);
        Assert.All(series.Values, point => Assert.Equal(0, point.TaskCount));

        // E o contador de tokens continua NULO em todos eles: "zero tasks" e
        // "nenhum token a relatar" são afirmações diferentes.
        Assert.All(series.Values, point => Assert.Equal(JsonValueKind.Null, point.TokenCount.ValueKind));

        // Contagem medida é 0; valor não coletado continua ausente, nunca 0.
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("tokens").GetProperty("conversation").GetProperty("inputTokens").ValueKind);
    }

    [Fact]
    public async Task InactiveAgent_IsStillQueryable()
    {
        var body = await GetAsync(InactiveAgentId);

        // Inatividade é estado de cadastro, não ausência de sujeito: o que ele
        // executou enquanto ativo foi medido.
        Assert.Equal(1, body.GetProperty("volume").GetProperty("executedTaskCount").GetInt32());
    }

    // ---------------------------------------------------------- A assimetria

    [Fact]
    public async Task DelegationSides_Diverge_AndTheGuardAssertsTheDivergence()
    {
        var source = await GetAsync(OnlyDelegatesAgentId);
        var target = await GetAsync(OnlyTriggeredAgentId);

        // Lado de quem TENTOU: 2 Completed + 1 NotStarted para o mesmo alvo.
        var attempted = source.GetProperty("delegation").GetProperty("delegatesTo").EnumerateArray()
            .Where(row => row.GetProperty("targetAgentId").GetGuid() == OnlyTriggeredAgentId)
            .Sum(row => row.GetProperty("count").GetInt32());

        // Lado de quem EXECUTOU: só as 2 que viraram execução.
        var executed = target.GetProperty("delegation").GetProperty("triggeredBy").EnumerateArray()
            .Where(row => row.GetProperty("sourceAgentId").GetGuid() == OnlyDelegatesAgentId)
            .Sum(row => row.GetProperty("executedCount").GetInt32());

        Assert.Equal(3, attempted);
        Assert.Equal(2, executed);

        // A ASSERÇÃO QUE IMPORTA: os dois lados da mesma relação DIVERGEM, e
        // isso é resultado correto. É este guarda que reprova o "conserto"
        // bem-intencionado de fazer os números baterem — um guarda que afirmasse
        // igualdade reprovaria o comportamento certo.
        Assert.NotEqual(attempted, executed);

        // E a divergência chega DECLARADA, não implícita.
        var caveats = source.GetProperty("delegation").GetProperty("caveats").EnumerateArray()
            .Select(row => row.GetString())
            .ToList();
        Assert.Contains("delegation-sides-are-not-mirrors", caveats);
    }

    [Fact]
    public async Task DelegationWithoutExecution_IsCountedOnOneSideOnly()
    {
        // Both delega para OnlyTriggered com Expired, e essa tentativa NÃO
        // produziu execução no alvo: o alvo não tem entrada nenhuma para Both.
        var source = await GetAsync(BothAgentId);
        var target = await GetAsync(OnlyTriggeredAgentId);

        Assert.Contains(
            source.GetProperty("delegation").GetProperty("delegatesTo").EnumerateArray(),
            row => row.GetProperty("targetAgentId").GetGuid() == OnlyTriggeredAgentId
                && row.GetProperty("outcome").GetString() == "Expired");

        Assert.DoesNotContain(
            target.GetProperty("delegation").GetProperty("triggeredBy").EnumerateArray(),
            row => row.GetProperty("sourceAgentId").GetGuid() == BothAgentId);
    }

    [Fact]
    public async Task AgentThatDoesBoth_HasBothSidesFilledAndSeparate()
    {
        var body = await GetAsync(BothAgentId);

        Assert.NotEmpty(body.GetProperty("delegation").GetProperty("delegatesTo").EnumerateArray());
        Assert.NotEmpty(body.GetProperty("delegation").GetProperty("triggeredBy").EnumerateArray());
    }

    [Fact]
    public async Task AgentThatOnlyDelegates_HasTheTriggeredSideEmpty()
    {
        var body = await GetAsync(OnlyDelegatesAgentId);

        Assert.NotEmpty(body.GetProperty("delegation").GetProperty("delegatesTo").EnumerateArray());
        Assert.Empty(body.GetProperty("delegation").GetProperty("triggeredBy").EnumerateArray());
    }

    [Fact]
    public async Task AgentThatIsOnlyTriggered_HasTheDelegatingSideEmpty()
    {
        var body = await GetAsync(OnlyTriggeredAgentId);

        Assert.Empty(body.GetProperty("delegation").GetProperty("delegatesTo").EnumerateArray());
        Assert.NotEmpty(body.GetProperty("delegation").GetProperty("triggeredBy").EnumerateArray());
    }

    // ------------------------------------------- O que o escopo não sustenta

    [Fact]
    public async Task IndexingWork_DoesNotReachTheAgent_EvenThroughTheKnowledgeBinding()
    {
        var body = await GetAsync(BothAgentId);

        // M30 não tem CAMPO no agregado do agente — a forma do tipo é a primeira
        // barreira, antes de qualquer consulta.
        Assert.False(body.GetProperty("errors").TryGetProperty("indexingFailures", out _));

        // M19 conta SÓ a busca (200), nunca a indexação (5.000) — e a base
        // indexada ESTÁ vinculada a este agente, que é o que torna o guarda
        // discriminante: sem o vínculo semeado, a implementação recusada (somar
        // pelo vínculo) também daria este resultado.
        Assert.Equal(
            200,
            body.GetProperty("tokens").GetProperty("searchEmbeddingInputTokens").GetInt64());
    }

    [Fact]
    public async Task EmbeddingMetric_DeclaresThatItCoversSearchOnly()
    {
        var body = await GetAsync(BothAgentId);

        var caveats = body.GetProperty("tokens").GetProperty("caveats").EnumerateArray()
            .Select(row => row.GetString())
            .ToList();

        Assert.Contains("embedding-covers-search-only", caveats);
    }

    // ------------------------------------------- O que muda de significado

    [Fact]
    public async Task Depth_IsTheAgentPosition_NotTheSystemChainLength()
    {
        var both = await GetAsync(BothAgentId);
        var deeper = await GetAsync(OnlyTriggeredAgentId);

        var bothDepth = both.GetProperty("performance").GetProperty("maxDepthAtWhichAgentRan").GetInt32();
        var deeperDepth = deeper.GetProperty("performance").GetProperty("maxDepthAtWhichAgentRan").GetInt32();

        // Existe na janela uma cadeia mais profunda que a maior posição em que
        // Both executou — e a profundidade de Both continua sendo a DELE.
        Assert.Equal(1, bothDepth);
        Assert.Equal(2, deeperDepth);
        Assert.True(bothDepth < deeperDepth);
    }

    // ------------------------------------------------------------ Balde local

    /// <summary>
    /// A ASSERÇÃO MUDOU DE FORMA pela mesma razão do gêmeo, e a razão vale
    /// literalmente igual: a ausência do dia UTC deixou de separar nada quando a
    /// série passou a cobrir todo dia medido. A forma nova afirma <b>onde a task
    /// caiu</b>, e balde em UTC reprova nas duas asserções.
    /// </summary>
    [Fact]
    public async Task DailyBucket_UsesTheLocalDay_NotTheUtcDay()
    {
        var body = await GetAsync(BothAgentId);

        var series = DailySeries(body);

        Assert.Contains(ExpectedLocalDay, series.Keys);
        Assert.Contains(UtcDay, series.Keys);

        Assert.Equal(1, series[ExpectedLocalDay].TaskCount);
        Assert.Equal(0, series[UtcDay].TaskCount);
    }

    /// <inheritdoc cref="DailyBucket_UsesTheLocalDay_NotTheUtcDay"/>
    [Fact]
    public async Task Weekday_FollowsTheLocalDay_NotTheUtcDay()
    {
        var body = await GetAsync(BothAgentId);

        var weekdays = Weekdays(body);

        Assert.Contains(ExpectedLocalWeekday, weekdays.Keys);
        Assert.Contains(UtcWeekday, weekdays.Keys);

        Assert.Equal(1, weekdays[ExpectedLocalWeekday]);
        Assert.Equal(0, weekdays[UtcWeekday]);
    }

    // ------------------------------------------------------------- Regime

    [Fact]
    public async Task RegimeStartWithOffset_DoesNotBecomeAnInternalError()
    {
        // A janela começa ANTES do regime, então é o instante de CONFIGURAÇÃO —
        // que o binder liga com deslocamento -03:00 intacto — que vira parâmetro
        // da consulta. Sem o ToUniversalTime() o Npgsql recusa e isto vira 500.
        var response = await _fixture.CreateClient()
            .GetAsync($"/insights/agents/{BothAgentId}?from=2026-08-01T00:00:00Z&to={WindowTo}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// <b>A PRECONDIÇÃO É O QUE FAZ ESTE GUARDA DISCRIMINAR</b>, acrescentada
    /// pela change <c>serie-diaria-dia-medido-vazio</c>: sem ela,
    /// <c>Assert.DoesNotContain</c> sobre lista vazia é verde, e a implementação
    /// que ele deveria prender era justamente uma que não emitia dia nenhum.
    /// </summary>
    [Fact]
    public async Task DaysBeforeTheRegime_AreOmitted_NeverEmittedAsZero()
    {
        var body = await GetAsync(BothAgentId, from: "2026-08-01T00:00:00Z");

        var days = DailySeries(body).Keys.ToList();

        // PRECONDIÇÃO: a série cobre o regime inteiro até o instante da consulta.
        Assert.Equal(23, days.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), days.Min());
        Assert.Equal(QueryInstantLocalDay, days.Max());

        // Nenhum dia anterior ao regime aparece — nem com 0, que afirmaria
        // medição que não houve.
        Assert.DoesNotContain(days, day => day < new DateOnly(2026, 9, 1));
    }

    // ------------------------------------------- Dia medido e sem ocorrência

    /// <summary>
    /// O par que faltava, no escopo do agente. A capability do agente declarava
    /// <b>só</b> a metade negativa — a positiva nunca foi escrita para este
    /// escopo, então aqui não havia contrato a violar, havia contrato faltando
    /// (design.md, D7).
    /// </summary>
    [Fact]
    public async Task MeasuredAndEmptyDay_AppearsInTheSeries_WithZero()
    {
        var body = await GetAsync(BothAgentId, GapWindowFrom, GapWindowTo);

        var series = DailySeries(body);

        Assert.Equal(
            [new(2026, 9, 14), new(2026, 9, 15), new(2026, 9, 16), new(2026, 9, 17)],
            series.Keys.ToList());

        Assert.Equal(1, series[new(2026, 9, 14)].TaskCount);
        Assert.Equal(0, series[new(2026, 9, 15)].TaskCount);
        Assert.Equal(1, series[new(2026, 9, 17)].TaskCount);
    }

    /// <summary>
    /// <b>O guarda que só existe neste escopo.</b> Em 16/09 rodaram execuções —
    /// de <c>OnlyDelegates</c> e de <c>OnlyTriggered</c> —, e nenhuma do agente
    /// consultado. O dia foi MEDIDO; o que deu zero foi a participação dele.
    /// Omiti-lo diria que ninguém mediu aquele dia, que é falso.
    /// </summary>
    [Fact]
    public async Task DayWhereOnlyAnotherAgentRan_IsStillMeasured_WithZero()
    {
        var body = await GetAsync(BothAgentId, GapWindowFrom, GapWindowTo);

        var series = DailySeries(body);

        Assert.Contains(new DateOnly(2026, 9, 16), series.Keys);
        Assert.Equal(0, series[new(2026, 9, 16)].TaskCount);
    }

    /// <inheritdoc cref="MeasuredAndEmptyDay_AppearsInTheSeries_WithZero"/>
    [Fact]
    public async Task MeasuredAndEmptyDay_HasNullTokenCount_NotZero()
    {
        var body = await GetAsync(BothAgentId, GapWindowFrom, GapWindowTo);

        var emptyDay = DailySeries(body)[new DateOnly(2026, 9, 15)];

        Assert.Equal(0, emptyDay.TaskCount);
        Assert.Equal(JsonValueKind.Null, emptyDay.TokenCount.ValueKind);
    }

    /// <summary>
    /// O simétrico do dia anterior ao regime (design.md, D3): emitir <c>0</c>
    /// para dia que ainda não aconteceu afirma medição sobre o futuro.
    /// </summary>
    [Fact]
    public async Task DaysAfterTheQueryInstant_AreNotEmittedAsZero()
    {
        var body = await GetAsync(BothAgentId, "2026-09-22T03:00:00Z", "2026-10-01T02:59:59Z");

        var days = DailySeries(body).Keys.ToList();

        Assert.Equal([new(2026, 9, 22), QueryInstantLocalDay], days);
        Assert.DoesNotContain(days, day => day > QueryInstantLocalDay);
    }

    // ------------------------------------------------------- Dia da semana

    /// <summary>
    /// A armadilha da forma (design.md, D4): <c>count(*)</c> com <c>left join</c>
    /// devolve <b>1</b> no dia sem correspondência. A asserção é contra <c>0</c>.
    /// </summary>
    [Fact]
    public async Task WeekdayCoveredAndEmpty_ArrivesAsZero()
    {
        var body = await GetAsync(BothAgentId, GapWindowFrom, GapWindowTo);

        var weekdays = Weekdays(body);

        // 14/09 segunda, 15/09 terça, 16/09 quarta, 17/09 quinta.
        Assert.Equal(1, weekdays[DayOfWeek.Monday]);
        Assert.Equal(0, weekdays[DayOfWeek.Tuesday]);
        Assert.Equal(0, weekdays[DayOfWeek.Wednesday]);
        Assert.Equal(1, weekdays[DayOfWeek.Thursday]);
    }

    /// <summary>
    /// O par do de cima: dia da semana que não ocorre na faixa medida nunca foi
    /// medido.
    /// </summary>
    [Fact]
    public async Task WeekdayNotCoveredByTheMeasuredRange_IsOmitted()
    {
        var body = await GetAsync(BothAgentId, GapWindowFrom, "2026-09-16T02:59:59Z");

        var weekdays = Weekdays(body);

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Tuesday], weekdays.Keys.OrderBy(day => day).ToList());
    }

    /// <summary>
    /// A regressão que a correção introduziria (design.md, D5): sete contagens
    /// iguais a zero e o <c>MaxBy</c> elegendo domingo como pico de um período
    /// em que o agente não fez nada.
    /// </summary>
    [Fact]
    public async Task PeriodMeasuredWithoutOccurrences_HasNoPeakWeekday()
    {
        var body = await GetAsync(BothAgentId, "2026-09-02T03:00:00Z", "2026-09-04T02:59:59Z");

        var weekdays = Weekdays(body);

        // PRECONDIÇÃO: há dias da semana na resposta, todos zerados.
        Assert.NotEmpty(weekdays);
        Assert.All(weekdays.Values, count => Assert.Equal(0, count));

        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("temporal").GetProperty("peakWeekday").ValueKind);
    }

    // ---------------------------------------------------------------- Nulo

    [Fact]
    public async Task UnreportedTokens_ArriveAsNull_NeverAsZero()
    {
        var body = await GetAsync(BothAgentId);

        var inputTokens = body.GetProperty("tokens").GetProperty("conversation").GetProperty("inputTokens");

        // A asserção NEGATIVA é a que vale: afirmar que não é 0 onde a fonte é
        // nula. A positiva passaria igual nos dois comportamentos.
        Assert.Equal(JsonValueKind.Null, inputTokens.ValueKind);
    }

    // ------------------------------------------------------- Autenticação

    [Fact]
    public async Task Route_WithoutToken_Returns401()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync($"/insights/agents/{BothAgentId}?from={WindowFrom}&to={WindowTo}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------ Apoio

    private async Task<JsonElement> GetAsync(Guid agentId, string? from = null, string? to = null)
    {
        var response = await _fixture.CreateClient()
            .GetAsync($"/insights/agents/{agentId}?from={from ?? WindowFrom}&to={to ?? WindowTo}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// O contador de tokens chega CRU, e não convertido: o guarda precisa
    /// distinguir <c>null</c> de <c>0</c>, e converter para <c>long?</c> aqui
    /// reintroduziria no teste o colapso que ele existe para proibir.
    /// </summary>
    private sealed record DailyPoint(int TaskCount, JsonElement TokenCount);

    private static SortedDictionary<DateOnly, DailyPoint> DailySeries(JsonElement body) =>
        new(body.GetProperty("temporal").GetProperty("dailySeries")
            .EnumerateArray()
            .ToDictionary(
                point => DateOnly.FromDateTime(point.GetProperty("day").GetDateTime()),
                point => new DailyPoint(
                    point.GetProperty("taskCount").GetInt32(),
                    point.GetProperty("tokenCount"))));

    private static Dictionary<DayOfWeek, int> Weekdays(JsonElement body) =>
        body.GetProperty("temporal").GetProperty("byWeekday")
            .EnumerateArray()
            .ToDictionary(
                point => (DayOfWeek)point.GetProperty("weekday").GetInt32(),
                point => point.GetProperty("taskCount").GetInt32());

    private sealed record SeededRowCounts(int Executions, int Delegations, int EmbeddingCalls, int Bindings);

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
            values ({BothAgentId}, 'Faz os dois', '', now(), now(), true, '[]'::jsonb),
                   ({OnlyDelegatesAgentId}, 'Só delega', '', now(), now(), true, '[]'::jsonb),
                   ({OnlyTriggeredAgentId}, 'Só é delegado', '', now(), now(), true, '[]'::jsonb),
                   ({EmptyAgentId}, 'Existe e está vazio', '', now(), now(), true, '[]'::jsonb),
                   ({InactiveAgentId}, 'Existe e está inativo', '', now(), now(), false, '[]'::jsonb);
            """);

        // 1) A task NOTURNA de Both — 2026-09-15T02:30Z é 14/09 23:30 em -03:00,
        //    outro dia E outro dia da semana. A chamada ao provedor NÃO reporta
        //    token: é a fonte do guarda de nulo negativo, e por isso Both não
        //    recebe nenhuma outra provider_call com token.
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into task_executions
                ("TaskId", "AgentId", "ContextId", "Provider", "Model", "Origin",
                 "DelegationDepth", "SubmittedAt", "StartedAt", "EndedAt", "TerminalState")
            values ('te-both-night', {BothAgentId}, 'ctx-1', 'openai', 'modelo-a', 'External',
                    0, timestamptz '2026-09-15T02:29:00Z', {NightInstant}::timestamptz,
                    timestamptz '2026-09-15T02:31:00Z', 'Completed');

            insert into provider_calls
                ("Id", "TaskId", "Provider", "Model", "Purpose", "DurationMs",
                 "InputTokens", "OutputTokens", "CachedInputTokens", "Failed")
            values (gen_random_uuid(), 'te-both-night', 'openai', 'modelo-a', 'Turn', 120.0,
                    null, null, null, false);
            """);

        // 2) A execução de origem de OnlyDelegates — é ela que situa no tempo o
        //    lado "Delega para", porque delegation_outcomes não tem coluna
        //    temporal utilizável.
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into task_executions
                ("TaskId", "AgentId", "ContextId", "Provider", "Model", "Origin",
                 "DelegationDepth", "SubmittedAt", "StartedAt", "EndedAt", "TerminalState")
            values ('te-onlydel-1', {OnlyDelegatesAgentId}, 'ctx-2', 'openai', 'modelo-do-vizinho', 'External',
                    0, timestamptz '2026-09-16T09:59:00Z', timestamptz '2026-09-16T10:00:00Z',
                    timestamptz '2026-09-16T10:10:00Z', 'Completed');

            insert into provider_calls
                ("Id", "TaskId", "Provider", "Model", "Purpose", "DurationMs",
                 "InputTokens", "OutputTokens", "CachedInputTokens", "Failed")
            values (gen_random_uuid(), 'te-onlydel-1', 'openai', 'modelo-do-vizinho', 'Turn', 300.0,
                    100, 50, 10, false);
            """);

        // 3) As DUAS execuções que rodaram em OnlyTriggered por delegação de
        //    OnlyDelegates. A segunda está em profundidade 2 — é a cadeia mais
        //    funda da janela, e o que torna o guarda de M26 discriminante.
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into task_executions
                ("TaskId", "AgentId", "ContextId", "Provider", "Model", "Origin",
                 "SourceAgentId", "SourceTaskId", "DelegationDepth",
                 "SubmittedAt", "StartedAt", "EndedAt", "TerminalState")
            values ('te-trig-1', {OnlyTriggeredAgentId}, 'ctx-2', 'openai', 'modelo-b', 'Delegation',
                    {OnlyDelegatesAgentId}, 'te-onlydel-1', 1,
                    timestamptz '2026-09-16T10:04:00Z', timestamptz '2026-09-16T10:05:00Z',
                    timestamptz '2026-09-16T10:06:00Z', 'Completed'),
                   ('te-trig-2', {OnlyTriggeredAgentId}, 'ctx-2', 'openai', 'modelo-b', 'Delegation',
                    {OnlyDelegatesAgentId}, 'te-onlydel-1', 2,
                    timestamptz '2026-09-16T10:06:00Z', timestamptz '2026-09-16T10:07:00Z',
                    timestamptz '2026-09-16T10:08:00Z', 'Completed');
            """);

        // 4) A execução que rodou em Both por delegação — é o que faz dele o
        //    cenário 3 do protótipo (delega E é delegado).
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into task_executions
                ("TaskId", "AgentId", "ContextId", "Provider", "Model", "Origin",
                 "SourceAgentId", "SourceTaskId", "DelegationDepth",
                 "SubmittedAt", "StartedAt", "EndedAt", "TerminalState")
            values ('te-both-trig', {BothAgentId}, 'ctx-2', 'openai', 'modelo-a', 'Delegation',
                    {OnlyDelegatesAgentId}, 'te-onlydel-1', 1,
                    timestamptz '2026-09-17T13:59:00Z', timestamptz '2026-09-17T14:00:00Z',
                    timestamptz '2026-09-17T14:01:00Z', 'Completed');
            """);

        // 5) A execução do agente INATIVO — inatividade é estado de cadastro, e
        //    o que ele executou enquanto ativo continua medido.
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into task_executions
                ("TaskId", "AgentId", "ContextId", "Provider", "Model", "Origin",
                 "DelegationDepth", "SubmittedAt", "StartedAt", "EndedAt", "TerminalState")
            values ('te-inactive', {InactiveAgentId}, 'ctx-3', 'openai', 'modelo-c', 'External',
                    0, timestamptz '2026-09-18T11:59:00Z', timestamptz '2026-09-18T12:00:00Z',
                    timestamptz '2026-09-18T12:00:30Z', 'Completed');
            """);

        // 6) Os resultados de delegação — o lado de quem TENTOU.
        //    OnlyDelegates → OnlyTriggered: 2 Completed (viraram execução) e 1
        //    NotStarted (NÃO virou). É essa linha que produz a divergência 3 × 2.
        //    Both → OnlyTriggered: 1 Expired, que também não virou execução.
        await dbContext.Database.ExecuteSqlAsync($"""
            insert into delegation_outcomes
                ("Id", "SourceTaskId", "SourceAgentId", "TargetAgentId", "TargetTaskId",
                 "Outcome", "LastObservedTargetState", "LastObservedAt", "SuccessfulReadCount", "DurationMs")
            values (gen_random_uuid(), 'te-onlydel-1', {OnlyDelegatesAgentId}, {OnlyTriggeredAgentId},
                    'te-trig-1', 'Completed', 'Completed', timestamptz '2026-09-16T10:06:00Z', 3, 60000.0),
                   (gen_random_uuid(), 'te-onlydel-1', {OnlyDelegatesAgentId}, {OnlyTriggeredAgentId},
                    'te-trig-2', 'Completed', 'Completed', timestamptz '2026-09-16T10:08:00Z', 3, 60000.0),
                   (gen_random_uuid(), 'te-onlydel-1', {OnlyDelegatesAgentId}, {OnlyTriggeredAgentId},
                    null, 'NotStarted', null, null, 0, 10.0),
                   (gen_random_uuid(), 'te-onlydel-1', {OnlyDelegatesAgentId}, {BothAgentId},
                    'te-both-trig', 'Completed', 'Completed', timestamptz '2026-09-17T14:01:00Z', 2, 60000.0),
                   (gen_random_uuid(), 'te-both-night', {BothAgentId}, {OnlyTriggeredAgentId},
                    null, 'Expired', 'Submitted', timestamptz '2026-09-15T02:30:30Z', 2, 5000.0);
            """);

        // 7) A base de conhecimento VINCULADA a Both, a tentativa de indexação
        //    dela, e as duas chamadas de embedding.
        //
        //    O vínculo é o que torna o guarda discriminante: sem ele, a
        //    implementação RECUSADA — somar a indexação pelo vínculo de base —
        //    também devolveria só os 200 da busca, e o guarda ficaria verde com e
        //    sem o defeito (convenção 15, primeira forma).
        var attemptId = Guid.NewGuid();

        await dbContext.Database.ExecuteSqlAsync($"""
            insert into knowledge_bases ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt")
            values ({KnowledgeBaseId}, 'Base do agente', 'vinculada a Both', true, now(), now());

            insert into agent_knowledge_bases ("AgentId", "KnowledgeBaseId")
            values ({BothAgentId}, {KnowledgeBaseId});

            insert into knowledge_indexing_attempts
                ("Id", "KnowledgeDocumentId", "KnowledgeBaseId", "ContentRevision", "Attempt",
                 "MaxAttempts", "StartedAt", "EndedAt", "Outcome", "FailurePhase", "FragmentCount")
            values ({attemptId}, gen_random_uuid(), {KnowledgeBaseId}, 1, 1, 3,
                    timestamptz '2026-09-16T08:00:00Z', timestamptz '2026-09-16T08:01:00Z',
                    'Failed', 'Embedding', null);
            """);

        await dbContext.Database.ExecuteSqlAsync($"""
            insert into embedding_calls
                ("Id", "Purpose", "KnowledgeIndexingAttemptId", "TaskId", "KnowledgeBaseId",
                 "Provider", "Model", "Dimensions", "InputCount", "DurationMs", "InputTokens", "Failed")
            values (gen_random_uuid(), 'Indexing', {attemptId}, null, {KnowledgeBaseId},
                    'openai', 'emb-3', 1536, 40, 900.0, 5000, false),
                   (gen_random_uuid(), 'Search', null, 'te-both-night', {KnowledgeBaseId},
                    'openai', 'emb-3', 1536, 1, 40.0, 200, false);
            """);

        var counts = await dbContext.Database.SqlQuery<SeededRowCounts>($"""
            select (select count(*) from task_executions)::int as "Executions",
                   (select count(*) from delegation_outcomes)::int as "Delegations",
                   (select count(*) from embedding_calls)::int as "EmbeddingCalls",
                   (select count(*) from agent_knowledge_bases)::int as "Bindings"
            """).SingleAsync();

        // A asserção da PRECONDIÇÃO, e não só a semeadura: é ela que impede todo
        // guarda deste arquivo de ficar verde sobre banco vazio.
        Assert.Equal(new SeededRowCounts(6, 5, 2, 1), counts);
    }
}
