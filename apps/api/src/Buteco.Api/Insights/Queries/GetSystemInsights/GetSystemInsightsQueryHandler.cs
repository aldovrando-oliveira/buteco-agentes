using Buteco.Api.Infrastructure;
using Buteco.Api.Insights.Responses;
using Buteco.Api.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Insights.Queries.GetSystemInsights;

/// <summary>
/// Toda agregação roda NO BANCO, e o balde diário sai em SQL com
/// <c>AT TIME ZONE</c> sobre o nome vindo de configuração (design.md, D7).
///
/// <para>
/// <b>Por que SQL cru e não LINQ:</b> o balde precisa de <c>AT TIME ZONE</c> com
/// um nome que é PARÂMETRO, e de <c>percentile_cont</c> — nenhum dos dois tem
/// tradução em LINQ. Escrever o balde em C# significaria materializar as linhas
/// para agrupar em memória, que é exatamente o que a rota recusa.
/// </para>
///
/// <para>
/// <b>Nenhum índice de expressão sustenta isto, e não deve sustentar:</b> o
/// filtro de janela é um range sobre o INSTANTE (que usa índice comum) e o
/// <c>AT TIME ZONE</c> agrupa o que já passou pelo filtro. Índice de expressão
/// congelaria o nome do fuso no schema, que é o oposto de mantê-lo em
/// configuração.
/// </para>
///
/// <para>
/// <b>Três das cinco tabelas de métrica não têm coluna temporal própria</b>
/// (<c>provider_calls</c> e <c>embedding_calls</c> não têm nenhuma;
/// <c>delegation_outcomes</c> tem só <c>LastObservedAt</c>, que é a última
/// leitura e não o instante do evento). Toda janela sobre elas é JOIN AO PAI, e
/// é por isso que esta rota não pode ser "uma consulta por tabela".
/// </para>
/// </summary>
public sealed class GetSystemInsightsQueryHandler(
    AppDbContext dbContext, IOptions<MetricsOptions> options, TimeProvider timeProvider)
    : IQueryHandler<GetSystemInsightsQuery, SystemInsightsResponse>
{
    /// <summary>
    /// O MESMO conjunto que a varredura periódica de <c>apps/workers</c> usa
    /// (<c>NonTerminalTaskDetector</c>). O protocolo tem nove estados, dos quais
    /// CINCO são não-terminais — <c>Unspecified</c>, <c>InputRequired</c> e
    /// <c>AuthRequired</c> também —, e nenhum dos três foi observado nesta base.
    ///
    /// <para>
    /// Adotar os cinco seria mais correto e está ERRADO agora: as duas fontes
    /// precisam ser comparadas em paralelo enquanto a <c>replicas-de-worker</c>
    /// decide capacidade, e conjuntos diferentes as fariam medir números
    /// diferentes justamente durante a comparação que as valida (design.md, D9).
    /// Passa aos cinco quando aquela change fechar, junto da remoção do detector.
    /// </para>
    /// </summary>
    private static readonly string[] NonTerminalStates = ["Submitted", "Working"];

    public async ValueTask<SystemInsightsResponse> Handle(
        GetSystemInsightsQuery query, CancellationToken cancellationToken)
    {
        var timeZone = options.Value.TimeZone!;
        var regimes = options.Value.Regimes;

        var executionRegime = RegimeStart(regimes, MetricsOptions.ExecutionRegime);
        var embeddingRegime = RegimeStart(regimes, MetricsOptions.EmbeddingRegime);
        var rejectionRegime = RegimeStart(regimes, MetricsOptions.RejectionRegime);

        // O início efetivo de cada grupo é o MAIS TARDE entre o pedido e o
        // regime: nada antes do regime é medido, e emitir 0 ali afirmaria
        // medição que não houve. Dias anteriores simplesmente não existem na
        // série (convenção 13).
        var executionFrom = Later(query.From, executionRegime);
        var embeddingFrom = Later(query.From, embeddingRegime);
        var rejectionFrom = Later(query.From, rejectionRegime);

        var window = new InsightsWindowResponse(query.From, query.To, timeZone);

        return new SystemInsightsResponse(
            window,
            regimes,
            await VolumeAsync(executionFrom, query.To, cancellationToken),
            await TemporalAsync(timeZone, executionFrom, SeriesEnd(query.To), cancellationToken),
            await TokensAsync(executionFrom, embeddingFrom, query.To, cancellationToken),
            await PerformanceAsync(executionFrom, query.To, cancellationToken),
            await ErrorsAsync(executionFrom, embeddingFrom, rejectionFrom, query.To, cancellationToken),
            await DelegationAsync(executionFrom, query.To, cancellationToken));
    }

    /// <summary>
    /// Normaliza para deslocamento zero, e a normalização NÃO é redundante com a
    /// da janela — foi um defeito real, pego pelo guarda.
    ///
    /// <para>
    /// Os limites <c>from</c>/<c>to</c> passam por <see cref="InsightsPeriod"/>,
    /// que aplica <c>AdjustToUniversal</c>. <b>O instante de regime não passa por
    /// lá:</b> ele vem da CONFIGURAÇÃO, e o binder liga
    /// <c>"2026-09-01T00:00:00-03:00"</c> a um <see cref="DateTimeOffset"/> com
    /// deslocamento <c>-03:00</c> intacto. Quando o regime é mais tarde que o
    /// pedido, é ele que vira parâmetro da consulta — e o Npgsql RECUSA
    /// deslocamento diferente de zero para coluna de instante
    /// (<c>ArgumentException</c> → 500).
    /// </para>
    ///
    /// <para>
    /// A lição é de alcance: a disciplina do <c>AdjustToUniversal</c> vale para
    /// TODO <see cref="DateTimeOffset"/> que chega ao driver, não só para o que
    /// veio de query string. Um valor de configuração entra por um caminho que
    /// nenhum parse cobre.
    /// </para>
    /// </summary>
    private static DateTimeOffset? RegimeStart(IReadOnlyDictionary<string, DateTimeOffset> regimes, string name) =>
        regimes.TryGetValue(name, out var start) ? start.ToUniversalTime() : null;

    private static DateTimeOffset Later(DateTimeOffset requested, DateTimeOffset? regimeStart) =>
        regimeStart is not null && regimeStart.Value > requested ? regimeStart.Value : requested;

    /// <summary>
    /// O limite SUPERIOR da série densa: o mais cedo entre o <c>to</c> pedido e o
    /// instante da consulta.
    ///
    /// <para>
    /// <b>É o simétrico do recorte de regime, e sem ele a correção da série
    /// criaria o defeito que ela existe para corrigir</b>, virado para o outro
    /// lado: o <c>generate_series</c> emitiria <c>0</c> para dias que AINDA NÃO
    /// ACONTECERAM, afirmando medição sobre o futuro. A rota não valida teto de
    /// intervalo, então um <c>to</c> no futuro é aceito.
    /// </para>
    ///
    /// <para>
    /// <b>O "agora" vem de <see cref="TimeProvider"/>, nunca de <c>now()</c> no
    /// SQL.</b> A alternativa foi recusada na D3 da <c>rotas-de-agregacao-sistema</c>:
    /// uma janela cujo "agora" nasce no banco não é verificável de forma
    /// determinística, e não haveria como afirmar em teste onde a série termina.
    /// </para>
    ///
    /// <para>
    /// Recorta <b>só a série temporal</b>. As demais agregações continuam usando
    /// o <c>to</c> pedido: elas somam o que existe, e linha de métrica com
    /// carimbo no futuro não existe. É a série densa que teria de INVENTAR o
    /// ponto.
    /// </para>
    /// </summary>
    private DateTimeOffset SeriesEnd(DateTimeOffset requestedTo)
    {
        var now = timeProvider.GetUtcNow();
        return requestedTo < now ? requestedTo : now;
    }

    // ---------------------------------------------------------------- Volume

    private sealed record VolumeRow(int ExecutedTaskCount, int ExternalOriginTaskCount);

    private async Task<VolumeInsightsResponse> VolumeAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // M2 e M1. Contagens MEDIDAS — zero aqui é verdade, e é por isso que
        // saem como int e não como int?.
        var row = await dbContext.Database
            .SqlQuery<VolumeRow>($"""
                select count(*)::int as "ExecutedTaskCount",
                       count(*) filter (where "Origin" = 'External')::int as "ExternalOriginTaskCount"
                from task_executions
                where "StartedAt" >= {from} and "StartedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        return new VolumeInsightsResponse(MetricsOptions.ExecutionRegime, row.ExecutedTaskCount, row.ExternalOriginTaskCount);
    }

    // -------------------------------------------------------------- Temporal

    private sealed record DailyRow(DateOnly Day, int TaskCount, long? TokenCount);

    private sealed record WeekdayRow(int Weekday, int TaskCount);

    private async Task<TemporalInsightsResponse> TemporalAsync(
        string timeZone, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // M10 e M7. A série nasce do DOMÍNIO DE DIAS, não das linhas existentes.
        //
        // Antes desta change era um group by sobre task_executions, e o efeito
        // era que um dia DENTRO do regime, medido e sem nenhuma task, sumia
        // exatamente como um dia ANTERIOR ao regime. A spec exige as duas
        // metades — omitir o dia não medido E emitir 0 para o dia medido e vazio
        // —, e só a primeira estava implementada. Com o generate_series, a
        // AUSÊNCIA de um dia passa a significar uma coisa só: não foi medido.
        //
        // OS DOIS LIMITES SÃO CONTRATO, e cada um tem o seu motivo:
        //
        //   inferior  `from` já é o recorte do regime (Later, acima). Gerar
        //             antes dele reintroduziria o defeito que a metade negativa
        //             proíbe — e seria PIOR que o anterior, porque emitiria 0
        //             em vez de omitir.
        //   superior  SeriesEnd recorta pelo instante da consulta. Sem isso, um
        //             `to` no futuro faria a série afirmar medição sobre dias
        //             que ainda não aconteceram.
        //
        // count(distinct) porque o join com provider_calls multiplica as linhas
        // da execução — count(*) daria o número de CHAMADAS com cara de número
        // de tasks, que sai maior e plausível.
        //
        // O token é nulo quando NENHUMA chamada do dia reportou número, e soma o
        // que se sabe quando alguma reportou: o filter preserva a distinção que
        // um coalesce solto apagaria. No dia medido e vazio ele é nulo por
        // construção — "foram zero tasks" e "não há token a relatar" são
        // afirmações diferentes, e o 0 de uma não vaza para a outra.
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
                      and e."StartedAt" >= {from} and e."StartedAt" <= {to}
                left join provider_calls pc on pc."TaskId" = e."TaskId"
                group by 1
                order by 1
                """)
            .ToListAsync(cancellationToken);

        // M6 — dia da semana LOCAL, não UTC. extract(dow) sobre o MESMO domínio
        // de dias da série: um dia da semana que ocorre na faixa medida e não
        // teve task chega com 0; um que NÃO ocorre nela é omitido, porque nunca
        // houve medição dele para dar zero.
        //
        // count(e."TaskId") E NÃO count(*): com o left join ao domínio de dias,
        // count(*) conta 1 para o dia sem correspondência, e todo dia da semana
        // vazio chegaria com 1 — número pequeno, positivo, sem sintoma nenhum.
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
                      and e."StartedAt" >= {from} and e."StartedAt" <= {to}
                group by 1
                order by 1
                """)
            .ToListAsync(cancellationToken);

        var byWeekday = weekday
            .Select(row => new WeekdayInsightPoint((DayOfWeek)row.Weekday, row.TaskCount))
            .ToList();

        // M9 — derivada de M6, e NULA quando não há nenhuma task no período:
        // sem medição não há pico, e inventar "domingo" seria afirmar o que não
        // se sabe.
        //
        // A SEGUNDA CONDIÇÃO SÓ PASSOU A SER NECESSÁRIA COM O DOMÍNIO DENSO, e
        // sem ela a correção acima criaria uma regressão de convenção 13: a
        // faixa medida sem nenhuma task agora devolve linhas ZERADAS em vez de
        // lista vazia, e o MaxBy elegeria a primeira delas — domingo — como pico
        // de um período em que nada aconteceu. As duas condições, não uma.
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

    private sealed record AgentTokenRow(Guid AgentId, long? InputTokens, long? OutputTokens);

    private sealed record ProviderTokenRow(string Provider, long? ConversationInputTokens, long? ConversationOutputTokens);

    private sealed record ProviderEmbeddingRow(string Provider, long? EmbeddingInputTokens);

    private sealed record ModelTokenRow(string Provider, string Model, long? TotalTokens, int CallCount);

    private sealed record PerTaskRow(double? Average, double? P95);

    private async Task<TokenInsightsResponse> TokensAsync(
        DateTimeOffset executionFrom, DateTimeOffset embeddingFrom, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // M11 e M12. sum() sobre coluna anulável devolve NULL quando nenhuma
        // linha reportou — e é assim que chega ao cliente. Normalizar para 0
        // aqui afirmaria "o provedor reportou zero token", que é outra coisa.
        var totals = await dbContext.Database
            .SqlQuery<TotalsRow>($"""
                select sum(pc."InputTokens")::bigint as "InputTokens",
                       sum(pc."OutputTokens")::bigint as "OutputTokens",
                       sum(pc."CachedInputTokens")::bigint as "CachedInputTokens"
                from provider_calls pc
                join task_executions e on e."TaskId" = pc."TaskId"
                where e."StartedAt" >= {executionFrom} and e."StartedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        // M19 — embedding tem regime PRÓPRIO e dois pais distintos conforme o
        // Purpose: a indexação pendura em knowledge_indexing_attempts, a busca
        // em task_executions. A janela precisa dos dois caminhos.
        var embeddingTokens = await dbContext.Database
            .SqlQuery<long?>($"""
                select sum(ec."InputTokens")::bigint as "Value"
                from embedding_calls ec
                left join knowledge_indexing_attempts kia on kia."Id" = ec."KnowledgeIndexingAttemptId"
                left join task_executions e on e."TaskId" = ec."TaskId"
                where coalesce(kia."StartedAt", e."StartedAt") >= {embeddingFrom}
                  and coalesce(kia."StartedAt", e."StartedAt") <= {to}
                """)
            .SingleAsync(cancellationToken);

        // M13 — provider_calls não tem AgentId; ele chega só pelo join ao pai.
        var byAgent = await dbContext.Database
            .SqlQuery<AgentTokenRow>($"""
                select e."AgentId" as "AgentId",
                       sum(pc."InputTokens")::bigint as "InputTokens",
                       sum(pc."OutputTokens")::bigint as "OutputTokens"
                from provider_calls pc
                join task_executions e on e."TaskId" = pc."TaskId"
                where e."StartedAt" >= {executionFrom} and e."StartedAt" <= {to}
                group by 1
                order by 1
                """)
            .ToListAsync(cancellationToken);

        var conversationByProvider = await dbContext.Database
            .SqlQuery<ProviderTokenRow>($"""
                select pc."Provider" as "Provider",
                       sum(pc."InputTokens")::bigint as "ConversationInputTokens",
                       sum(pc."OutputTokens")::bigint as "ConversationOutputTokens"
                from provider_calls pc
                join task_executions e on e."TaskId" = pc."TaskId"
                where e."StartedAt" >= {executionFrom} and e."StartedAt" <= {to}
                group by 1
                """)
            .ToListAsync(cancellationToken);

        var embeddingByProvider = await dbContext.Database
            .SqlQuery<ProviderEmbeddingRow>($"""
                select ec."Provider" as "Provider",
                       sum(ec."InputTokens")::bigint as "EmbeddingInputTokens"
                from embedding_calls ec
                left join knowledge_indexing_attempts kia on kia."Id" = ec."KnowledgeIndexingAttemptId"
                left join task_executions e on e."TaskId" = ec."TaskId"
                where coalesce(kia."StartedAt", e."StartedAt") >= {embeddingFrom}
                  and coalesce(kia."StartedAt", e."StartedAt") <= {to}
                group by 1
                """)
            .ToListAsync(cancellationToken);

        // M14 — o ÚNICO nível em que conversa e embedding se somam. A união é
        // pelo NOME do provedor, e um provedor que só apareça de um lado entra
        // com nulo do outro, nunca com zero.
        var providerNames = conversationByProvider.Select(row => row.Provider)
            .Union(embeddingByProvider.Select(row => row.Provider))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var byProvider = providerNames
            .Select(name =>
            {
                var conversation = conversationByProvider.SingleOrDefault(row => row.Provider == name);
                var embedding = embeddingByProvider.SingleOrDefault(row => row.Provider == name);
                return new ProviderTokenResponse(
                    name,
                    conversation?.ConversationInputTokens,
                    conversation?.ConversationOutputTokens,
                    embedding?.EmbeddingInputTokens);
            })
            .ToList();

        // M15, M16a e M16b numa consulta só: o ranking por tokens e o ranking
        // por número de chamadas são ORDENAÇÕES da mesma linha, não duas
        // medições. CallCount é contagem medida — pode ser zero de verdade.
        var byModel = await dbContext.Database
            .SqlQuery<ModelTokenRow>($"""
                select pc."Provider" as "Provider",
                       pc."Model" as "Model",
                       sum(coalesce(pc."InputTokens", 0) + coalesce(pc."OutputTokens", 0))
                           filter (where pc."InputTokens" is not null or pc."OutputTokens" is not null)::bigint as "TotalTokens",
                       count(*)::int as "CallCount"
                from provider_calls pc
                join task_executions e on e."TaskId" = pc."TaskId"
                where e."StartedAt" >= {executionFrom} and e."StartedAt" <= {to}
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
                    where e."StartedAt" >= {executionFrom} and e."StartedAt" <= {to}
                    group by e."TaskId"
                ) as per_task
                """)
            .SingleAsync(cancellationToken);

        return new TokenInsightsResponse(
            MetricsOptions.ExecutionRegime,
            MetricsOptions.EmbeddingRegime,
            new TokenTotalsResponse(totals.InputTokens, totals.OutputTokens, totals.CachedInputTokens),
            embeddingTokens,
            byAgent.Select(row => new AgentTokenResponse(row.AgentId, row.InputTokens, row.OutputTokens)).ToList(),
            byProvider,
            byModel.Select(row => new ModelTokenResponse(row.Provider, row.Model, row.TotalTokens, row.CallCount)).ToList(),
            new TokensPerTaskResponse(perTask.Average, perTask.P95));
    }

    // ----------------------------------------------------------- Desempenho

    private sealed record StatsRow(double? AverageMs, double? P95Ms, int SampleCount);

    private sealed record CallsPerTaskRow(double? Value);

    private sealed record DepthRow(int? Value);

    private async Task<PerformanceInsightsResponse> PerformanceAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // M21 — e o SampleCount é o que torna o desconto visível. SubmittedAt é
        // NULO em reentrega (a task não estava em Submitted), e essas execuções
        // ficam FORA do cálculo: entrar com zero inventaria uma duração de zero
        // milissegundo que nunca aconteceu.
        var taskDuration = await StatsAsync(
            $"""
            select avg(ms)::double precision as "AverageMs",
                   percentile_cont(0.95) within group (order by ms)::double precision as "P95Ms",
                   count(*)::int as "SampleCount"
            from (
                select extract(epoch from ("EndedAt" - "SubmittedAt")) * 1000 as ms
                from task_executions
                where "StartedAt" >= {from} and "StartedAt" <= {to}
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
                where "StartedAt" >= {from} and "StartedAt" <= {to}
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
            where e."StartedAt" >= {from} and e."StartedAt" <= {to}
            """, cancellationToken);

        // M24.
        var callsPerTask = await dbContext.Database
            .SqlQuery<CallsPerTaskRow>($"""
                select avg(calls)::double precision as "Value"
                from (
                    select count(pc."Id")::double precision as calls
                    from task_executions e
                    left join provider_calls pc on pc."TaskId" = e."TaskId"
                    where e."StartedAt" >= {from} and e."StartedAt" <= {to}
                    group by e."TaskId"
                ) as per_task
                """)
            .SingleAsync(cancellationToken);

        // M25 — e o NOME da métrica é o achado. O resíduo é a duração da
        // execução menos a soma das chamadas ao provedor, e contém ferramentas
        // MAIS espera de lock, chamadas a servidores MCP e busca vetorial.
        // LockAcquiredAt permitiria descontar o lock; o resto não é separável
        // com as colunas que existem. Por isso não se chama "tempo em tools":
        // o rótulo afirmaria mais do que o número sabe (convenção 13).
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
                where e."StartedAt" >= {from} and e."StartedAt" <= {to}
                  and e."EndedAt" is not null
                group by e."TaskId", e."StartedAt", e."EndedAt"
            ) as residuals
            """, cancellationToken);

        // M26 — profundidade OBSERVADA, e nula quando não houve observação.
        var depth = await dbContext.Database
            .SqlQuery<DepthRow>($"""
                select max("DelegationDepth")::int as "Value"
                from task_executions
                where "StartedAt" >= {from} and "StartedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        return new PerformanceInsightsResponse(
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

    private sealed record AgentFailureRow(Guid AgentId, string? Provider, string? Model, int FailedCount);

    private sealed record PhaseRow(string Phase, int Count);

    private sealed record RejectionReasonRow(string Reason, int Count);

    private sealed record IndexingFailureRow(string Outcome, string? FailurePhase, int Count);

    private sealed record NonTerminalRow(int OpenExecutionCount, int NeverConsumedCount);

    private async Task<ErrorInsightsResponse> ErrorsAsync(
        DateTimeOffset executionFrom,
        DateTimeOffset embeddingFrom,
        DateTimeOffset rejectionFrom,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        // M27 — failed e rejected SEPARADOS, e este `rejected` conta uma população
        // ESPECÍFICA: a recusa que TEM linha de execução, que hoje é só a de
        // profundidade de delegação (AgentExecutionService, único sítio que grava
        // TerminalState = 'Rejected').
        //
        // A recusa de entrada, feita por apps/api, continua sem linha de execução —
        // e desde a change recusa-motivo-coleta ela tem FONTE PRÓPRIA
        // (`task_rejections`), servida abaixo em `rejectedAtEntryCount`. Este
        // comentário dizia "a parte que falta só existe em a2a_tasks", e era
        // verdade quando foi escrito: a etapa 5 o tornou falso, e corrigi-lo aqui é
        // a convenção 13 aplicada a comentário.
        //
        // O caveat `rejections-missing-from-executions` FICA, porque o que ele diz
        // continua valendo: esta contagem não inclui a recusa de entrada, e ela
        // também não entra no percentual de falha — nem no numerador, nem no
        // denominador.
        var terminal = await dbContext.Database
            .SqlQuery<TerminalRow>($"""
                select count(*) filter (where "TerminalState" = 'Failed')::int as "FailedCount",
                       count(*) filter (where "TerminalState" = 'Rejected')::int as "RejectedCount"
                from task_executions
                where "StartedAt" >= {executionFrom} and "StartedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        // M28.
        var byAgent = await dbContext.Database
            .SqlQuery<AgentFailureRow>($"""
                select "AgentId" as "AgentId", "Provider" as "Provider", "Model" as "Model",
                       count(*)::int as "FailedCount"
                from task_executions
                where "StartedAt" >= {executionFrom} and "StartedAt" <= {to}
                  and "TerminalState" in ('Failed', 'Rejected')
                group by 1, 2, 3
                order by 4 desc, 1
                """)
            .ToListAsync(cancellationToken);

        // M29, primeira metade — a FASE da falha de execução, e não texto de
        // exceção. O vocabulário aqui é o `FailurePhase` de task_executions, com
        // sete valores que a capability agent-execution-metrics enumera.
        //
        // A outra metade de M29 — o motivo da RECUSA DE ENTRADA — chega em lista
        // própria (`rejectionsByReason`), e não aqui: são vocabulários diferentes,
        // de tabelas diferentes, e misturá-los faria este campo afirmar valores que
        // o contrato dele não tem. É a tela que concatena as duas listas, que é o
        // que o protótipo desenha.
        var byPhase = await dbContext.Database
            .SqlQuery<PhaseRow>($"""
                select "FailurePhase" as "Phase", count(*)::int as "Count"
                from task_executions
                where "StartedAt" >= {executionFrom} and "StartedAt" <= {to}
                  and "FailurePhase" is not null
                group by 1
                order by 2 desc, 1
                """)
            .ToListAsync(cancellationToken);

        // M29 — O MOTIVO DA RECUSA DE ENTRADA, que até a change
        // recusa-motivo-coleta não tinha fonte nenhuma (era o caveat
        // `rejection-reason-not-collected`, que sai desta lista).
        //
        // JANELA DO REGIME PRÓPRIO: `rejectionFrom`, e não `executionFrom`. A
        // coleta começou depois das outras duas, e usar a janela de execução
        // devolveria 0 para período em que a coluna não existia — "medi e não achei"
        // onde a verdade é ausência de medição (convenção 13).
        //
        // A CONTAGEM E OS MOTIVOS SAEM DA MESMA JANELA, e é isso que faz a soma dos
        // motivos fechar com a contagem: a coluna de motivo é obrigatória, então
        // toda linha contada aqui tem um motivo contado ali.
        var rejectedAtEntry = await dbContext.Database
            .SqlQuery<int>($"""
                select count(*)::int as "Value"
                from task_rejections
                where "RejectedAt" >= {rejectionFrom} and "RejectedAt" <= {to}
                """)
            .SingleAsync(cancellationToken);

        // Desempate por "Reason" depois da contagem: sem ele, duas causas com a
        // mesma contagem sairiam em ordem que o plano decide (convenção 15, quinta
        // forma — ordem que o banco às vezes já produz sozinho).
        var byReason = await dbContext.Database
            .SqlQuery<RejectionReasonRow>($"""
                select "Reason" as "Reason", count(*)::int as "Count"
                from task_rejections
                where "RejectedAt" >= {rejectionFrom} and "RejectedAt" <= {to}
                group by 1
                order by 2 desc, 1
                """)
            .ToListAsync(cancellationToken);

        // M30 — regime de embedding, não de execução.
        var indexingFailures = await dbContext.Database
            .SqlQuery<IndexingFailureRow>($"""
                select "Outcome" as "Outcome", "FailurePhase" as "FailurePhase", count(*)::int as "Count"
                from knowledge_indexing_attempts
                where "StartedAt" >= {embeddingFrom} and "StartedAt" <= {to}
                  and "Outcome" in ('Failed', 'RetryScheduled')
                group by 1, 2
                order by 3 desc, 1
                """)
            .ToListAsync(cancellationToken);

        // M32 — leitura DO INSTANTE, sem janela, e é assim de propósito:
        // a2a_tasks sobrescreve status_timestamp a cada transição e não guarda
        // histórico, então não há como reconstruir quantas estavam abertas num
        // momento passado. É o que separa esta métrica do detector de
        // apps/workers, que amostra uma SÉRIE — e é por isso que ela não o
        // substitui (design.md, D10).
        var nonTerminal = await dbContext.Database
            .SqlQuery<NonTerminalRow>($"""
                select (select count(*) from task_executions where "EndedAt" is null)::int as "OpenExecutionCount",
                       (select count(*)
                        from a2a_tasks t
                        left join task_executions e on e."TaskId" = t.task_id
                        where e."TaskId" is null and t.state = any({NonTerminalStates}))::int as "NeverConsumedCount"
                """)
            .SingleAsync(cancellationToken);

        // OS TRÊS REGIMES, e nenhum eleito para representar o grupo: este bloco lê
        // task_executions (execução), knowledge_indexing_attempts (embedding) e
        // task_rejections (recusa). Um nome só daria a três coletas a data de uma.
        return new ErrorInsightsResponse(
            MetricsOptions.ExecutionRegime,
            MetricsOptions.EmbeddingRegime,
            MetricsOptions.RejectionRegime,
            terminal.FailedCount,
            terminal.RejectedCount,
            rejectedAtEntry,
            byAgent.Select(row => new AgentFailureResponse(row.AgentId, row.Provider, row.Model, row.FailedCount)).ToList(),
            byPhase.Select(row => new FailurePhaseResponse(row.Phase, row.Count)).ToList(),
            byReason.Select(row => new RejectionReasonResponse(row.Reason, row.Count)).ToList(),
            indexingFailures.Select(row => new IndexingFailureResponse(row.Outcome, row.FailurePhase, row.Count)).ToList(),
            new NonTerminalTasksResponse(nonTerminal.OpenExecutionCount, nonTerminal.NeverConsumedCount, NonTerminalStates),
            [
                // `rejection-reason-not-collected` SAIU aqui: a lacuna que ele
                // descrevia deixou de existir, e código de parcialidade que
                // sobrevive à lacuna afirma limitação que não há.
                InsightsCaveats.RejectionsMissingFromExecutions,
                InsightsCaveats.PointInTimeOnly,
            ]);
    }

    // ------------------------------------------------------------- Delegação

    private sealed record DelegationPairRow(Guid SourceAgentId, Guid TargetAgentId, string Outcome, int Count);

    private async Task<DelegationInsightsResponse> DelegationAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // M34, lado "Delega para". delegation_outcomes NÃO tem coluna temporal
        // utilizável — LastObservedAt é a última leitura e é anulável —, então a
        // janela vem do pai por SourceTaskId.
        var pairs = await dbContext.Database
            .SqlQuery<DelegationPairRow>($"""
                select d."SourceAgentId" as "SourceAgentId",
                       d."TargetAgentId" as "TargetAgentId",
                       d."Outcome" as "Outcome",
                       count(*)::int as "Count"
                from delegation_outcomes d
                join task_executions e on e."TaskId" = d."SourceTaskId"
                where e."StartedAt" >= {from} and e."StartedAt" <= {to}
                group by 1, 2, 3
                order by 4 desc, 1, 2, 3
                """)
            .ToListAsync(cancellationToken);

        return new DelegationInsightsResponse(
            MetricsOptions.ExecutionRegime,
            pairs.Select(row => new DelegationPairResponse(row.SourceAgentId, row.TargetAgentId, row.Outcome, row.Count)).ToList());
    }
}
