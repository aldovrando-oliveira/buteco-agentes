using Buteco.Api.Infrastructure;
using Buteco.Api.Insights.Responses;
using Buteco.Api.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Insights.Queries.GetAgentInsights;

/// <summary>
/// O agregado do escopo de UM agente — e ele <b>não</b> é o do sistema com um
/// <c>where</c> a mais (design.md, D1).
///
/// <para>
/// <b>Nove das 27 métricas do catálogo não transferem por analogia.</b> Duas não
/// existem neste escopo (M13, porque o recorte já é o agente; M30, porque
/// <c>knowledge_indexing_attempts</c> não tem coluna de agente); quatro são outra
/// consulta (M14, M19, M28, M34); e quatro mudam de SIGNIFICADO com a mesma
/// consulta (M15, M16a, M16b, M26). As outras 17 são a mesma consulta com o
/// filtro do agente. O mapa inteiro está no <c>design.md</c>.
/// </para>
///
/// <para>
/// <b>Toda agregação roda NO BANCO</b>, e o balde diário sai em SQL com
/// <c>AT TIME ZONE</c> sobre o nome vindo de configuração — o mesmo contrato da
/// rota do sistema, herdado e não redecidido. SQL cru pelo mesmo motivo do
/// gêmeo: <c>AT TIME ZONE</c> com nome PARAMETRIZADO e <c>percentile_cont</c> não
/// têm tradução em LINQ, e escrever o balde em C# significaria materializar as
/// linhas para agrupar em memória.
/// </para>
///
/// <para>
/// <b>O filtro central cai em índice existente</b> —
/// <c>task_executions(AgentId)</c> e <c>delegation_outcomes(SourceAgentId)</c>
/// existem, lidos no <c>AppDbContext</c>. Nenhum índice novo entra, e nenhum
/// índice de expressão sobre a conversão de fuso (D12).
/// </para>
/// </summary>
public sealed class GetAgentInsightsQueryHandler(
    AppDbContext dbContext, IOptions<MetricsOptions> options, TimeProvider timeProvider)
    : IQueryHandler<GetAgentInsightsQuery, AgentInsightsResponse?>
{
    /// <summary>
    /// O MESMO conjunto do detector de <c>apps/workers</c> e da rota do sistema.
    /// O protocolo tem cinco estados não-terminais; adotar conjunto diferente
    /// aqui faria as três fontes medirem números diferentes justamente enquanto
    /// precisam ser comparadas (design.md, D9 — e D9 da change A).
    /// </summary>
    private static readonly string[] NonTerminalStates = ["Submitted", "Working"];

    private const string ExternalOrigin = "External";

    private const string DelegationOrigin = "Delegation";

    public async ValueTask<AgentInsightsResponse?> Handle(
        GetAgentInsightsQuery query, CancellationToken cancellationToken)
    {
        // A EXISTÊNCIA é checada antes de qualquer agregação, e é a única leitura
        // que esta rota faz de `agents` — nenhum campo do agente entra no
        // agregado. Agente inexistente é 404; agente INATIVO é 200, porque
        // inatividade é estado de cadastro e o que ele executou enquanto ativo
        // foi medido (design.md, D3).
        var agentExists = await dbContext.Agents
            .AsNoTracking()
            .AnyAsync(agent => agent.Id == query.AgentId, cancellationToken);

        if (!agentExists)
        {
            return null;
        }

        var timeZone = options.Value.TimeZone!;
        var regimes = options.Value.Regimes;

        var executionRegime = RegimeStart(regimes, MetricsOptions.ExecutionRegime);
        var embeddingRegime = RegimeStart(regimes, MetricsOptions.EmbeddingRegime);

        var executionFrom = Later(query.From, executionRegime);
        var embeddingFrom = Later(query.From, embeddingRegime);

        var window = new InsightsWindowResponse(query.From, query.To, timeZone);

        return new AgentInsightsResponse(
            query.AgentId,
            window,
            regimes,
            await VolumeAsync(query.AgentId, executionFrom, query.To, cancellationToken),
            await TemporalAsync(query.AgentId, timeZone, executionFrom, SeriesEnd(query.To), cancellationToken),
            await TokensAsync(query.AgentId, executionFrom, embeddingFrom, query.To, cancellationToken),
            await PerformanceAsync(query.AgentId, executionFrom, query.To, cancellationToken),
            await ErrorsAsync(query.AgentId, executionFrom, query.To, cancellationToken),
            await DelegationAsync(query.AgentId, executionFrom, query.To, cancellationToken));
    }

    /// <summary>
    /// Normaliza para deslocamento zero, e a normalização <b>não é redundante</b>
    /// com a de <c>InsightsPeriod</c> — é o gêmeo de
    /// <c>GetSystemInsightsQueryHandler.RegimeStart</c>, e existe pelo defeito
    /// real que a change A pegou com o guarda dela.
    ///
    /// <para>
    /// Os limites <c>from</c>/<c>to</c> passam por <c>InsightsPeriod</c>, que
    /// aplica <c>AdjustToUniversal</c>. <b>O instante de regime não passa por
    /// lá:</b> vem da CONFIGURAÇÃO, e o binder liga
    /// <c>"2026-09-01T00:00:00-03:00"</c> a um <see cref="DateTimeOffset"/> com
    /// deslocamento <c>-03:00</c> intacto. Quando o regime é mais tarde que o
    /// pedido, é ELE que vira parâmetro da consulta — e o Npgsql recusa
    /// deslocamento diferente de zero para coluna de instante
    /// (<c>ArgumentException</c> → 500).
    /// </para>
    ///
    /// <para>
    /// A lição é de alcance, e é por isso que ela se repete aqui em vez de ser
    /// confiada à cópia: vale para TODO <see cref="DateTimeOffset"/> que chega ao
    /// driver, e valor de configuração entra por um caminho que nenhum parse
    /// cobre (design.md, D10).
    /// </para>
    /// </summary>
    private static DateTimeOffset? RegimeStart(IReadOnlyDictionary<string, DateTimeOffset> regimes, string name) =>
        regimes.TryGetValue(name, out var start) ? start.ToUniversalTime() : null;

    // O início efetivo é o MAIS TARDE entre o pedido e o regime: nada antes do
    // regime foi medido, e emitir 0 ali afirmaria medição que não houve. Dias
    // anteriores simplesmente não existem na série (convenção 13).
    private static DateTimeOffset Later(DateTimeOffset requested, DateTimeOffset? regimeStart) =>
        regimeStart is not null && regimeStart.Value > requested ? regimeStart.Value : requested;

    /// <summary>
    /// O limite SUPERIOR da série densa — o mais cedo entre o <c>to</c> pedido e
    /// o instante da consulta. <b>Gêmeo de
    /// <c>GetSystemInsightsQueryHandler.SeriesEnd</c></b>, e a duplicação é
    /// deliberada pelo mesmo motivo das duas consultas temporais: ver o
    /// comentário de <see cref="TemporalAsync"/>.
    ///
    /// <para>
    /// Sem ele, o <c>generate_series</c> emitiria <c>0</c> para dias que ainda
    /// não aconteceram — o simétrico do defeito que o recorte de regime evita no
    /// passado. O "agora" vem de <see cref="TimeProvider"/> e nunca de
    /// <c>now()</c> no SQL, que a D3 da <c>rotas-de-agregacao-sistema</c>
    /// recusou por tornar a janela não verificável em teste.
    /// </para>
    /// </summary>
    private DateTimeOffset SeriesEnd(DateTimeOffset requestedTo)
    {
        var now = timeProvider.GetUtcNow();
        return requestedTo < now ? requestedTo : now;
    }

    // ---------------------------------------------------------------- Volume

    private sealed record VolumeRow(int ExecutedTaskCount, int ExternalOriginTaskCount, int DelegationOriginTaskCount);

    private async Task<AgentVolumeInsightsResponse> VolumeAsync(
        Guid agentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // M2 e M1, com o filtro do agente. O recorte por Origin sai na mesma
        // consulta porque o protótipo aprovado exibe "Tasks executadas 97 · 97
        // por delegação" na mesma linha — e é ESSE número que precisa fechar com
        // a soma de "Acionado por" (D16 de metricas-execucao-coleta).
        var row = await dbContext.Database
            .SqlQuery<VolumeRow>($"""
                select count(*)::int as "ExecutedTaskCount",
                       count(*) filter (where "Origin" = {ExternalOrigin})::int as "ExternalOriginTaskCount",
                       count(*) filter (where "Origin" = {DelegationOrigin})::int as "DelegationOriginTaskCount"
                from task_executions
                where "AgentId" = {agentId}
                  and "StartedAt" >= {from} and "StartedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        return new AgentVolumeInsightsResponse(
            MetricsOptions.ExecutionRegime,
            row.ExecutedTaskCount,
            row.ExternalOriginTaskCount,
            row.DelegationOriginTaskCount);
    }

    // -------------------------------------------------------------- Temporal

    private sealed record DailyRow(DateOnly Day, int TaskCount, long? TokenCount);

    private sealed record WeekdayRow(int Weekday, int TaskCount);

    private async Task<TemporalInsightsResponse> TemporalAsync(
        Guid agentId, string timeZone, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // M10 e M7. A série nasce do DOMÍNIO DE DIAS, não das linhas existentes.
        //
        // COPIADA DO GÊMEO, NÃO EXTRAÍDA — GetSystemInsightsQueryHandler.
        // TemporalAsync. São consultas DIFERENTES, não a mesma com um parâmetro:
        // esta filtra por AgentId em dois pontos e a de lá não tem o conceito.
        // Um método comum com Guid? opcional e dois caminhos internos esconderia
        // a diferença em vez de compartilhar a semelhança. Mesmo tratamento das
        // entidades de métrica duplicadas entre apps/api e apps/workers, e cada
        // cópia tem o seu guarda: o verde de lá não cobre esta.
        //
        // O QUE MUDA NO ESCOPO DO AGENTE, e é o que o guarda próprio afirma: um
        // dia em que o SISTEMA mediu e este agente não executou nada é um dia
        // MEDIDO, e chega com 0. Omiti-lo diria que ninguém mediu aquele dia.
        //
        // OS DOIS LIMITES: o inferior já é o recorte do regime (Later); gerar
        // antes dele emitiria 0 onde não houve medição. O superior é SeriesEnd,
        // que impede a série de afirmar medição sobre dias futuros.
        //
        // O balde é o DIA LOCAL, nunca o dia UTC.
        var daily = await dbContext.Database
            .SqlQuery<DailyRow>($"""
                select d.day::date as "Day",
                       count(distinct e."TaskId")::int as "TaskCount",
                       sum(coalesce(pc."InputTokens", 0) + coalesce(pc."OutputTokens", 0))
                           filter (where pc."InputTokens" is not null or pc."OutputTokens" is not null)::bigint as "TokenCount"
                from generate_series(
                         ({from} at time zone {timeZone})::date,
                         ({to} at time zone {timeZone})::date,
                         interval '1 day') as d(day)
                left join task_executions e
                       on (e."StartedAt" at time zone {timeZone})::date = d.day::date
                      and e."AgentId" = {agentId}
                      and e."StartedAt" >= {from} and e."StartedAt" <= {to}
                left join provider_calls pc on pc."TaskId" = e."TaskId"
                group by 1
                order by 1
                """)
            .ToListAsync(cancellationToken);

        // M6 — dia da semana LOCAL sobre o MESMO domínio de dias. 0 = domingo,
        // mesmo mapeamento de DayOfWeek.
        //
        // count(e."TaskId") E NÃO count(*): com o left join ao domínio, count(*)
        // conta 1 no dia sem correspondência, e todo dia da semana vazio
        // chegaria com 1 — pequeno, positivo, sem sintoma.
        var weekday = await dbContext.Database
            .SqlQuery<WeekdayRow>($"""
                select extract(dow from d.day)::int as "Weekday",
                       count(e."TaskId")::int as "TaskCount"
                from generate_series(
                         ({from} at time zone {timeZone})::date,
                         ({to} at time zone {timeZone})::date,
                         interval '1 day') as d(day)
                left join task_executions e
                       on (e."StartedAt" at time zone {timeZone})::date = d.day::date
                      and e."AgentId" = {agentId}
                      and e."StartedAt" >= {from} and e."StartedAt" <= {to}
                group by 1
                order by 1
                """)
            .ToListAsync(cancellationToken);

        var byWeekday = weekday
            .Select(row => new WeekdayInsightPoint((DayOfWeek)row.Weekday, row.TaskCount))
            .ToList();

        // M9 — derivada de M6, e NULA quando não há nenhuma task: sem medição não
        // há pico, e inventar "domingo" seria afirmar o que não se sabe.
        //
        // A SEGUNDA CONDIÇÃO só passou a ser necessária com o domínio denso: a
        // faixa medida sem nenhuma task do agente agora devolve linhas ZERADAS
        // em vez de lista vazia, e o MaxBy elegeria domingo como pico de um
        // período em que ele não fez nada.
        DayOfWeek? peak = byWeekday.Count == 0 || byWeekday.Max(point => point.TaskCount) == 0
            ? null
            : byWeekday.MaxBy(point => point.TaskCount)!.Weekday;

        return new TemporalInsightsResponse(
            MetricsOptions.ExecutionRegime,
            daily.Select(row => new DailyInsightPoint(row.Day, row.TaskCount, row.TokenCount)).ToList(),
            byWeekday,
            peak);
    }

    // ---------------------------------------------------------------- Tokens

    private sealed record TotalsRow(long? InputTokens, long? OutputTokens, long? CachedInputTokens);

    private sealed record ProviderTokenRow(string Provider, long? ConversationInputTokens, long? ConversationOutputTokens);

    private sealed record ProviderEmbeddingRow(string Provider, long? SearchEmbeddingInputTokens);

    private sealed record ModelTokenRow(string Provider, string Model, long? TotalTokens, int CallCount);

    private sealed record PerTaskRow(double? Average, double? P95);

    private async Task<AgentTokenInsightsResponse> TokensAsync(
        Guid agentId,
        DateTimeOffset executionFrom,
        DateTimeOffset embeddingFrom,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        // M11 e M12. provider_calls NÃO tem AgentId — o agente chega só pelo join
        // ao pai, que é o mesmo motivo pelo qual a rota não pode ser "uma
        // consulta por tabela". O join cai em provider_calls(TaskId).
        var totals = await dbContext.Database
            .SqlQuery<TotalsRow>($"""
                select sum(pc."InputTokens")::bigint as "InputTokens",
                       sum(pc."OutputTokens")::bigint as "OutputTokens",
                       sum(pc."CachedInputTokens")::bigint as "CachedInputTokens"
                from provider_calls pc
                join task_executions e on e."TaskId" = pc."TaskId"
                where e."AgentId" = {agentId}
                  and e."StartedAt" >= {executionFrom} and e."StartedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        // M19 — e aqui está a diferença de escopo que mais engana. No sistema,
        // embedding tem DOIS pais conforme o Purpose; aqui só o de BUSCA alcança
        // agente, porque o de indexação pendura em knowledge_indexing_attempts,
        // que não tem coluna de agente.
        //
        // O caminho por AgentKnowledgeBases foi RECUSADO: o vínculo é de muitos
        // para muitos, e uma indexação que aconteceu uma vez seria contada em
        // cada agente vinculado — o total por agente somaria mais que o do
        // sistema (design.md, D6).
        var searchEmbeddingTokens = await dbContext.Database
            .SqlQuery<long?>($"""
                select sum(ec."InputTokens")::bigint as "Value"
                from embedding_calls ec
                join task_executions e on e."TaskId" = ec."TaskId"
                where e."AgentId" = {agentId}
                  and e."StartedAt" >= {embeddingFrom} and e."StartedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        var conversationByProvider = await dbContext.Database
            .SqlQuery<ProviderTokenRow>($"""
                select pc."Provider" as "Provider",
                       sum(pc."InputTokens")::bigint as "ConversationInputTokens",
                       sum(pc."OutputTokens")::bigint as "ConversationOutputTokens"
                from provider_calls pc
                join task_executions e on e."TaskId" = pc."TaskId"
                where e."AgentId" = {agentId}
                  and e."StartedAt" >= {executionFrom} and e."StartedAt" <= {to}
                group by 1
                """)
            .ToListAsync(cancellationToken);

        var embeddingByProvider = await dbContext.Database
            .SqlQuery<ProviderEmbeddingRow>($"""
                select ec."Provider" as "Provider",
                       sum(ec."InputTokens")::bigint as "SearchEmbeddingInputTokens"
                from embedding_calls ec
                join task_executions e on e."TaskId" = ec."TaskId"
                where e."AgentId" = {agentId}
                  and e."StartedAt" >= {embeddingFrom} and e."StartedAt" <= {to}
                group by 1
                """)
            .ToListAsync(cancellationToken);

        // M14 — o único nível em que conversa e embedding se somam, e neste
        // escopo a soma é PARCIAL por construção: falta a metade de indexação. O
        // nome do campo diz isso, e o caveat também.
        var providerNames = conversationByProvider.Select(row => row.Provider)
            .Union(embeddingByProvider.Select(row => row.Provider))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var byProvider = providerNames
            .Select(name =>
            {
                var conversation = conversationByProvider.SingleOrDefault(row => row.Provider == name);
                var embedding = embeddingByProvider.SingleOrDefault(row => row.Provider == name);
                return new AgentProviderTokenResponse(
                    name,
                    conversation?.ConversationInputTokens,
                    conversation?.ConversationOutputTokens,
                    embedding?.SearchEmbeddingInputTokens);
            })
            .ToList();

        // M15, M16a e M16b. MUDAM DE SIGNIFICADO neste escopo: mais de uma linha
        // aqui significa que ESTE agente mudou de provedor ou de modelo dentro da
        // janela, nunca que agentes diferentes usam valores diferentes.
        //
        // Isso é possível porque Provider/Model de task_executions são SNAPSHOT
        // do agente no início da execução — a entidade registra por escrito que é
        // assim de propósito, sem FK para agents, para que o consumo de ontem
        // pertença ao modelo de ontem.
        var byModel = await dbContext.Database
            .SqlQuery<ModelTokenRow>($"""
                select pc."Provider" as "Provider",
                       pc."Model" as "Model",
                       sum(coalesce(pc."InputTokens", 0) + coalesce(pc."OutputTokens", 0))
                           filter (where pc."InputTokens" is not null or pc."OutputTokens" is not null)::bigint as "TotalTokens",
                       count(*)::int as "CallCount"
                from provider_calls pc
                join task_executions e on e."TaskId" = pc."TaskId"
                where e."AgentId" = {agentId}
                  and e."StartedAt" >= {executionFrom} and e."StartedAt" <= {to}
                group by 1, 2
                order by 3 desc nulls last, 4 desc, 1, 2
                """)
            .ToListAsync(cancellationToken);

        // M17.
        var perTask = await dbContext.Database
            .SqlQuery<PerTaskRow>($"""
                select avg(total)::double precision as "Average",
                       percentile_cont(0.95) within group (order by total)::double precision as "P95"
                from (
                    select sum(coalesce(pc."InputTokens", 0) + coalesce(pc."OutputTokens", 0))
                               filter (where pc."InputTokens" is not null or pc."OutputTokens" is not null)::bigint as total
                    from provider_calls pc
                    join task_executions e on e."TaskId" = pc."TaskId"
                    where e."AgentId" = {agentId}
                      and e."StartedAt" >= {executionFrom} and e."StartedAt" <= {to}
                    group by e."TaskId"
                ) as per_task
                """)
            .SingleAsync(cancellationToken);

        return new AgentTokenInsightsResponse(
            MetricsOptions.ExecutionRegime,
            MetricsOptions.EmbeddingRegime,
            new TokenTotalsResponse(totals.InputTokens, totals.OutputTokens, totals.CachedInputTokens),
            searchEmbeddingTokens,
            byProvider,
            byModel.Select(row => new ModelTokenResponse(row.Provider, row.Model, row.TotalTokens, row.CallCount)).ToList(),
            new TokensPerTaskResponse(perTask.Average, perTask.P95),
            [AgentInsightsCaveats.EmbeddingCoversSearchOnly]);
    }

    // ------------------------------------------------------------ Desempenho

    private sealed record StatsRow(double? AverageMs, double? P95Ms, int SampleCount);

    private sealed record CallsPerTaskRow(double? Value);

    private sealed record DepthRow(int? Value);

    private async Task<AgentPerformanceInsightsResponse> PerformanceAsync(
        Guid agentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // M21 — SubmittedAt é NULO em reentrega, e essas execuções ficam FORA do
        // cálculo. O SampleCount é o que torna o desconto visível, em vez de
        // escondê-lo numa média que parece completa.
        var taskDuration = await StatsAsync(
            $"""
            select avg(ms)::double precision as "AverageMs",
                   percentile_cont(0.95) within group (order by ms)::double precision as "P95Ms",
                   count(*)::int as "SampleCount"
            from (
                select extract(epoch from ("EndedAt" - "SubmittedAt")) * 1000 as ms
                from task_executions
                where "AgentId" = {agentId}
                  and "StartedAt" >= {from} and "StartedAt" <= {to}
                  and "SubmittedAt" is not null and "EndedAt" is not null
            ) as durations
            """, cancellationToken);

        // M22 — mesma anulabilidade, mesmo desconto.
        var queueTime = await StatsAsync(
            $"""
            select avg(ms)::double precision as "AverageMs",
                   percentile_cont(0.95) within group (order by ms)::double precision as "P95Ms",
                   count(*)::int as "SampleCount"
            from (
                select extract(epoch from ("StartedAt" - "SubmittedAt")) * 1000 as ms
                from task_executions
                where "AgentId" = {agentId}
                  and "StartedAt" >= {from} and "StartedAt" <= {to}
                  and "SubmittedAt" is not null
            ) as durations
            """, cancellationToken);

        // M23.
        var providerCallDuration = await StatsAsync(
            $"""
            select avg(pc."DurationMs")::double precision as "AverageMs",
                   percentile_cont(0.95) within group (order by pc."DurationMs")::double precision as "P95Ms",
                   count(*)::int as "SampleCount"
            from provider_calls pc
            join task_executions e on e."TaskId" = pc."TaskId"
            where e."AgentId" = {agentId}
              and e."StartedAt" >= {from} and e."StartedAt" <= {to}
            """, cancellationToken);

        // M24.
        var callsPerTask = await dbContext.Database
            .SqlQuery<CallsPerTaskRow>($"""
                select avg(calls)::double precision as "Value"
                from (
                    select count(pc."Id")::double precision as calls
                    from task_executions e
                    left join provider_calls pc on pc."TaskId" = e."TaskId"
                    where e."AgentId" = {agentId}
                      and e."StartedAt" >= {from} and e."StartedAt" <= {to}
                    group by e."TaskId"
                ) as per_task
                """)
            .SingleAsync(cancellationToken);

        // M25 — o resíduo, e o NOME é o achado herdado: contém ferramentas MAIS
        // espera de lock, chamadas a servidores MCP e busca vetorial. Por isso
        // não se chama "tempo em tools".
        var residual = await StatsAsync(
            $"""
            select avg(ms)::double precision as "AverageMs",
                   percentile_cont(0.95) within group (order by ms)::double precision as "P95Ms",
                   count(*)::int as "SampleCount"
            from (
                select extract(epoch from (e."EndedAt" - e."StartedAt")) * 1000
                       - coalesce(sum(pc."DurationMs"), 0) as ms
                from task_executions e
                left join provider_calls pc on pc."TaskId" = e."TaskId"
                where e."AgentId" = {agentId}
                  and e."StartedAt" >= {from} and e."StartedAt" <= {to}
                  and e."EndedAt" is not null
                group by e."TaskId", e."StartedAt", e."EndedAt"
            ) as residuals
            """, cancellationToken);

        // M26 — MESMA consulta do sistema, OUTRA pergunta (design.md, D7). Lá é o
        // tamanho da maior cadeia que o sistema alcançou; aqui é a posição mais
        // profunda em que ESTE agente executou. Nula quando não houve observação.
        var depth = await dbContext.Database
            .SqlQuery<DepthRow>($"""
                select max("DelegationDepth")::int as "Value"
                from task_executions
                where "AgentId" = {agentId}
                  and "StartedAt" >= {from} and "StartedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        return new AgentPerformanceInsightsResponse(
            MetricsOptions.ExecutionRegime,
            taskDuration,
            queueTime,
            providerCallDuration,
            callsPerTask.Value,
            residual,
            depth.Value,
            [InsightsCaveats.SubmittedAtMissingOnRedelivery, InsightsCaveats.ResidualIsNotOnlyTools]);
    }

    private async Task<DurationStatsResponse> StatsAsync(
        FormattableString sql, CancellationToken cancellationToken)
    {
        var row = await dbContext.Database.SqlQuery<StatsRow>(sql).SingleAsync(cancellationToken);
        return new DurationStatsResponse(row.AverageMs, row.P95Ms, row.SampleCount);
    }

    // ------------------------------------------------------------------ Erros

    private sealed record TerminalRow(int FailedCount, int RejectedCount);

    private sealed record ModelFailureRow(string? Provider, string? Model, int FailedCount);

    private sealed record PhaseRow(string Phase, int Count);

    private sealed record NonTerminalRow(int OpenExecutionCount, int NeverConsumedCount);

    private async Task<AgentErrorInsightsResponse> ErrorsAsync(
        Guid agentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // M27 — e a parcialidade herdada continua valendo: recusas feitas por
        // apps/api nunca produzem linha de execução, então esta contagem
        // SUBCONTA também no escopo do agente.
        //
        // Neste escopo a lacuna é QUANTIFICÁVEL — a2a_tasks tem agent_id —, e
        // mesmo assim NÃO é contada aqui (design.md, D8): a change A não a contou
        // no escopo do sistema, e contá-la só de um lado faria os dois escopos
        // medirem coisas diferentes com o mesmo rótulo. Item com gatilho no 02:
        // quando a change de coleta do motivo fechar, decidir para OS DOIS
        // escopos ao mesmo tempo.
        var terminal = await dbContext.Database
            .SqlQuery<TerminalRow>($"""
                select count(*) filter (where "TerminalState" = 'Failed')::int as "FailedCount",
                       count(*) filter (where "TerminalState" = 'Rejected')::int as "RejectedCount"
                from task_executions
                where "AgentId" = {agentId}
                  and "StartedAt" >= {from} and "StartedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        // M28 SEM o agrupamento por agente — ele some quando o recorte já é o
        // agente, e o que resta é provedor/modelo daquele agente (design.md, D7).
        var byProviderAndModel = await dbContext.Database
            .SqlQuery<ModelFailureRow>($"""
                select "Provider" as "Provider", "Model" as "Model",
                       count(*)::int as "FailedCount"
                from task_executions
                where "AgentId" = {agentId}
                  and "StartedAt" >= {from} and "StartedAt" <= {to}
                  and "TerminalState" in ('Failed', 'Rejected')
                group by 1, 2
                order by 3 desc, 1, 2
                """)
            .ToListAsync(cancellationToken);

        // M29 — fase, e não texto de exceção. O motivo das recusas feitas por
        // apps/api continua sem fonte nenhuma, aqui como no escopo do sistema.
        var byPhase = await dbContext.Database
            .SqlQuery<PhaseRow>($"""
                select "FailurePhase" as "Phase", count(*)::int as "Count"
                from task_executions
                where "AgentId" = {agentId}
                  and "StartedAt" >= {from} and "StartedAt" <= {to}
                  and "FailurePhase" is not null
                group by 1
                order by 2 desc, 1
                """)
            .ToListAsync(cancellationToken);

        // M30 NÃO ENTRA: knowledge_indexing_attempts tem documento e base e
        // nenhuma coluna de agente, e atribuí-la pelo vínculo de base contaria a
        // mesma tentativa em cada agente vinculado (design.md, D6). Não há campo
        // para ela no DTO, de propósito — lista vazia afirmaria "medi e não achei
        // nada" onde não há fonte.

        // M32 — leitura DO INSTANTE, sem janela, e as DUAS populações têm coluna
        // de agente: a execução aberta por AgentId, e a task nunca consumida por
        // a2a_tasks.agent_id.
        var nonTerminal = await dbContext.Database
            .SqlQuery<NonTerminalRow>($"""
                select (select count(*)
                        from task_executions
                        where "AgentId" = {agentId} and "EndedAt" is null)::int as "OpenExecutionCount",
                       (select count(*)
                        from a2a_tasks t
                        left join task_executions e on e."TaskId" = t.task_id
                        where t.agent_id = {agentId}
                          and e."TaskId" is null
                          and t.state = any({NonTerminalStates}))::int as "NeverConsumedCount"
                """)
            .SingleAsync(cancellationToken);

        return new AgentErrorInsightsResponse(
            MetricsOptions.ExecutionRegime,
            terminal.FailedCount,
            terminal.RejectedCount,
            byProviderAndModel
                .Select(row => new AgentModelFailureResponse(row.Provider, row.Model, row.FailedCount))
                .ToList(),
            byPhase.Select(row => new FailurePhaseResponse(row.Phase, row.Count)).ToList(),
            new AgentNonTerminalTasksResponse(
                nonTerminal.OpenExecutionCount, nonTerminal.NeverConsumedCount, NonTerminalStates),
            [
                InsightsCaveats.RejectionsMissingFromExecutions,
                InsightsCaveats.RejectionReasonNotCollected,
                InsightsCaveats.PointInTimeOnly,
            ]);
    }

    // -------------------------------------------------------------- Delegação

    private sealed record DelegatesToRow(Guid TargetAgentId, string Outcome, int Count);

    private sealed record TriggeredByRow(Guid SourceAgentId, int ExecutedCount);

    /// <summary>
    /// <b>A decisão central desta rota.</b> Os dois lados leem FONTES DIFERENTES,
    /// e nenhum é derivado do outro (D16 de <c>metricas-execucao-coleta</c>).
    ///
    /// <para>
    /// <b>E eles podem divergir, por DUAS causas independentes</b> (design.md,
    /// D2). A primeira é o resultado que não produz execução: <c>NotStarted</c>
    /// nunca cria task no destino, e <c>Expired</c> pode ter criado uma que nunca
    /// rodou. A segunda é que <b>os dois lados são situados por relógios
    /// diferentes</b> — "delega para" pela execução de ORIGEM (a janela vem do
    /// pai por <c>SourceTaskId</c>, porque <c>delegation_outcomes</c> não tem
    /// coluna temporal utilizável: <c>LastObservedAt</c> é a última leitura e não
    /// o instante do evento) e "acionado por" pela execução de DESTINO, que é a
    /// própria linha. Uma delegação disparada às 23:58 cuja task roda às 00:03
    /// tem os seus dois lados em dias diferentes do balde.
    /// </para>
    ///
    /// <para>
    /// <b>Não "consertar" para os números baterem.</b> Há guarda que AFIRMA a
    /// divergência, e ele existe exatamente para reprovar essa correção
    /// bem-intencionada. Um guarda que afirmasse igualdade reprovaria o
    /// comportamento correto.
    /// </para>
    ///
    /// <para>
    /// <b>Recusado:</b> situar os dois lados pelo mesmo relógio. Quebraria a soma
    /// do protótipo aprovado — <i>"Tasks executadas 97 · 97 por delegação"</i>
    /// compara com as execuções do próprio agente na janela, situadas pelo
    /// <c>StartedAt</c> delas, e trocar o relógio de um lado faria o card
    /// discordar do número logo acima dele, na mesma tela.
    /// </para>
    /// </summary>
    private async Task<AgentDelegationInsightsResponse> DelegationAsync(
        Guid agentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // "Delega para" — o que o agente TENTOU. Filtro em
        // delegation_outcomes(SourceAgentId), que TEM índice. Discriminado por
        // Outcome porque é ele que explica a divergência.
        var delegatesTo = await dbContext.Database
            .SqlQuery<DelegatesToRow>($"""
                select d."TargetAgentId" as "TargetAgentId",
                       d."Outcome" as "Outcome",
                       count(*)::int as "Count"
                from delegation_outcomes d
                join task_executions e on e."TaskId" = d."SourceTaskId"
                where d."SourceAgentId" = {agentId}
                  and e."StartedAt" >= {from} and e."StartedAt" <= {to}
                group by 1, 2
                order by 3 desc, 1, 2
                """)
            .ToListAsync(cancellationToken);

        // "Acionado por" — o que de fato RODOU no agente. Fonte OUTRA
        // (task_executions), relógio OUTRO (o StartedAt da própria linha), e é
        // esta contagem que fecha com "tasks executadas por delegação" do card de
        // volume, na mesma tela.
        var triggeredBy = await dbContext.Database
            .SqlQuery<TriggeredByRow>($"""
                select "SourceAgentId" as "SourceAgentId",
                       count(*)::int as "ExecutedCount"
                from task_executions
                where "AgentId" = {agentId}
                  and "Origin" = {DelegationOrigin}
                  and "SourceAgentId" is not null
                  and "StartedAt" >= {from} and "StartedAt" <= {to}
                group by 1
                order by 2 desc, 1
                """)
            .ToListAsync(cancellationToken);

        return new AgentDelegationInsightsResponse(
            MetricsOptions.ExecutionRegime,
            delegatesTo
                .Select(row => new DelegatesToResponse(row.TargetAgentId, row.Outcome, row.Count))
                .ToList(),
            triggeredBy
                .Select(row => new TriggeredByResponse(row.SourceAgentId, row.ExecutedCount))
                .ToList(),
            [AgentInsightsCaveats.DelegationSidesAreNotMirrors]);
    }
}
