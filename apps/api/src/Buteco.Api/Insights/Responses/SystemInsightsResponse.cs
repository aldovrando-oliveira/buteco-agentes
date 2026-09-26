namespace Buteco.Api.Insights.Responses;

/// <summary>
/// O agregado inteiro da página de Insights do sistema.
///
/// <para>
/// <b>Objeto agregado, nunca linha bruta</b> — e a forma destes records é o que
/// impede a linha bruta de entrar por descuido de mapeamento: não há nenhum
/// campo que aceite uma coleção de entidades. Devolver linha bruta levaria
/// milhares de linhas ao navegador E colapsaria nulo em zero na agregação do
/// cliente, que é precisamente a distinção que a convenção 13 exige aqui.
/// </para>
///
/// <para>
/// <b>Todo campo numérico anulável é nulo por PROVENIÊNCIA, nunca por
/// conveniência.</b> Nulo = o sistema não sabe; <c>0</c> = a agregação percorreu
/// o período e não achou nada. Nenhum ponto deste caminho normaliza um pelo
/// outro.
/// </para>
/// </summary>
public sealed record SystemInsightsResponse(
    InsightsWindowResponse Window,
    IReadOnlyDictionary<string, DateTimeOffset> Regimes,
    VolumeInsightsResponse Volume,
    TemporalInsightsResponse Temporal,
    TokenInsightsResponse Tokens,
    PerformanceInsightsResponse Performance,
    ErrorInsightsResponse Errors,
    DelegationInsightsResponse Delegation);

/// <summary>A janela efetivamente usada, e o fuso em que o balde diário foi feito.</summary>
public sealed record InsightsWindowResponse(DateTimeOffset From, DateTimeOffset To, string TimeZone);

/// <summary>
/// Códigos estáveis de parcialidade. A rota não apresenta métrica parcial como
/// se fosse completa (convenção 13); estes códigos são como a lacuna chega ao
/// cliente, em vez de prosa que a tela teria de interpretar.
/// </summary>
public static class InsightsCaveats
{
    /// <summary>
    /// Recusas feitas por <c>apps/api</c> não produzem linha de execução, então
    /// <c>RejectedCount</c> — derivado das tabelas de métrica — conta OUTRA
    /// população: as recusas que uma execução registrou, que hoje são só as de
    /// profundidade de delegação. Afeta M27 e M28.
    ///
    /// <para>
    /// <b>Este código FICOU depois da change <c>recusa-motivo-coleta</c></b>, e o
    /// texto dele não mudou, porque continua inteiramente verdadeiro: a recusa de
    /// entrada segue sem linha de execução e segue fora do percentual de falha —
    /// nem no numerador, nem no denominador. O que a change acrescentou foi
    /// <c>RejectedAtEntryCount</c> e os motivos, em campos próprios.
    /// </para>
    ///
    /// <para>
    /// <b>E M28 continua parcial POR CONSTRUÇÃO:</b> duas das quatro causas de
    /// recusa são justamente a ausência de provedor ou modelo utilizáveis, então
    /// não há como agrupar a recusa de entrada por provedor e modelo. Este código
    /// não cai enquanto esse agrupamento existir.
    /// </para>
    /// </summary>
    public const string RejectionsMissingFromExecutions = "rejections-missing-from-executions";

    /// <summary>
    /// Duração e tempo de fila dependem do carimbo de submissão, que é nulo em
    /// reentrega — as execuções nesse estado ficam FORA do cálculo, como
    /// ausentes e nunca como zero. Afeta M21 e M22.
    /// </summary>
    public const string SubmittedAtMissingOnRedelivery = "submitted-at-missing-on-redelivery";

    /// <summary>
    /// O resíduo inclui, além de ferramentas, espera de lock, chamadas a
    /// servidores externos e busca vetorial. Afeta M25 — e é por isso que a
    /// métrica não se chama "tempo em tools".
    /// </summary>
    public const string ResidualIsNotOnlyTools = "residual-is-not-only-tools";

    /// <summary>
    /// Leitura DO INSTANTE da consulta, não série: o store durável de tasks
    /// sobrescreve o carimbo a cada transição e não guarda histórico. Afeta M32.
    /// </summary>
    public const string PointInTimeOnly = "point-in-time-only";
}

/// <summary>M1 e M2.</summary>
public sealed record VolumeInsightsResponse(
    string Regime,
    int ExecutedTaskCount,
    int ExternalOriginTaskCount);

/// <summary>M6, M7, M9 e M10.</summary>
public sealed record TemporalInsightsResponse(
    string Regime,
    IReadOnlyList<DailyInsightPoint> DailySeries,
    IReadOnlyList<WeekdayInsightPoint> ByWeekday,
    DayOfWeek? PeakWeekday);

/// <summary>
/// Um dia LOCAL da série, e a série é DENSA: existe um ponto para <b>todo</b> dia
/// medido da janela, inclusive o dia em que nada aconteceu, que chega com
/// <c>TaskCount = 0</c>.
///
/// <para>
/// <b>A ausência de um dia significa uma coisa só: ele não foi medido.</b> Dia
/// anterior ao início do regime é OMITIDO, nunca emitido com <c>0</c>; dia
/// posterior ao instante da consulta também, porque emitir <c>0</c> para um dia
/// que ainda não aconteceu afirmaria medição sobre o futuro. É esta densidade
/// que torna "não medido" distinguível de "medido e sem uso" — e é o que permite
/// ao cliente decidir o estado de um dia lendo a série, sem cruzá-la com o mapa
/// de regimes.
/// </para>
///
/// <para>
/// <b>Isto foi corrigido pela change <c>serie-diaria-dia-medido-vazio</c>, e o
/// texto anterior afirmava mais do que a consulta fazia</b> (convenção 9). Ele
/// dizia que "só existem pontos para dias dentro do regime", o que descrevia
/// apenas a metade negativa: a consulta era um <c>group by</c> sobre as linhas
/// existentes, e o dia medido e vazio sumia exatamente como o dia não medido. A
/// spec exigia as duas metades desde a change que criou a rota; só uma tinha
/// implementação, e só uma tinha guarda.
/// </para>
///
/// <para>
/// <c>TokenCount</c> permanece anulável e chega <b>nulo</b> no dia medido e
/// vazio: "foram zero tasks" e "não há token a relatar" são afirmações
/// diferentes, e o <c>0</c> de uma não vaza para a outra.
/// </para>
/// </summary>
public sealed record DailyInsightPoint(DateOnly Day, int TaskCount, long? TokenCount);

public sealed record WeekdayInsightPoint(DayOfWeek Weekday, int TaskCount);

/// <summary>M11 a M17 e M19.</summary>
public sealed record TokenInsightsResponse(
    string ConversationRegime,
    string EmbeddingRegime,
    TokenTotalsResponse Conversation,
    long? EmbeddingInputTokens,
    IReadOnlyList<AgentTokenResponse> ByAgent,
    IReadOnlyList<ProviderTokenResponse> ByProvider,
    IReadOnlyList<ModelTokenResponse> ByModel,
    TokensPerTaskResponse PerTask);

/// <summary>
/// M12. Nulo em qualquer dos três significa "nenhuma chamada do período reportou
/// esse número", e nunca zero — <c>UsageDetails</c> não obriga o provedor a
/// reportar.
/// </summary>
public sealed record TokenTotalsResponse(long? InputTokens, long? OutputTokens, long? CachedInputTokens);

public sealed record AgentTokenResponse(Guid AgentId, long? InputTokens, long? OutputTokens);

/// <summary>
/// M14 — o ÚNICO nível em que conversa e embedding se somam, por decisão do
/// dono. <c>EmbeddingInputTokens</c> vem de outro regime de medição, e a soma só
/// é honesta quando os dois regimes cobrem a janela pedida.
/// </summary>
public sealed record ProviderTokenResponse(
    string Provider,
    long? ConversationInputTokens,
    long? ConversationOutputTokens,
    long? EmbeddingInputTokens);

/// <summary>M15, M16a e M16b. <c>CallCount</c> é contagem medida — pode ser zero.</summary>
public sealed record ModelTokenResponse(string Provider, string Model, long? TotalTokens, int CallCount);

/// <summary>M17.</summary>
public sealed record TokensPerTaskResponse(double? Average, double? P95);

/// <summary>M21 a M26.</summary>
public sealed record PerformanceInsightsResponse(
    string Regime,
    DurationStatsResponse TaskDuration,
    DurationStatsResponse QueueTime,
    DurationStatsResponse ProviderCallDuration,
    double? ProviderCallsPerTask,
    DurationStatsResponse NonProviderResidual,
    int? MaxObservedDelegationDepth,
    IReadOnlyList<string> Caveats);

/// <summary>
/// <c>SampleCount</c> é quantas execuções entraram no cálculo — não quantas
/// existem. É ele que torna visível o desconto das execuções sem carimbo de
/// submissão, em vez de escondê-lo numa média que parece completa.
/// </summary>
public sealed record DurationStatsResponse(double? AverageMs, double? P95Ms, int SampleCount);

/// <summary>M27 a M30 e M32.</summary>
public sealed record ErrorInsightsResponse(
    string ExecutionRegime,
    string IndexingRegime,
    string RejectionRegime,
    int FailedCount,
    int RejectedCount,
    int RejectedAtEntryCount,
    IReadOnlyList<AgentFailureResponse> ByAgent,
    IReadOnlyList<FailurePhaseResponse> ByPhase,
    IReadOnlyList<RejectionReasonResponse> RejectionsByReason,
    IReadOnlyList<IndexingFailureResponse> IndexingFailures,
    NonTerminalTasksResponse NonTerminal,
    IReadOnlyList<string> Caveats);

/// <summary>
/// M29 — um valor do vocabulário fechado de motivo de recusa e a contagem dele na
/// janela, no MESMO formato de <see cref="FailurePhaseResponse"/>, que é o que o
/// card de Motivos já consome: lista rasa, rótulo e contagem, sem
/// subcardinalidade.
///
/// <para>
/// <b>Lista PRÓPRIA, e não dentro de <c>ByPhase</c></b>: o vocabulário de
/// <c>ByPhase</c> é o <c>FailurePhase</c> de <c>task_executions</c>, que a
/// capability <c>agent-execution-metrics</c> enumera em sete valores. Enfiar um
/// motivo de recusa ali seria mentir no contrato de outra capability — a tela
/// concatena as duas listas, que é o que o protótipo desenha.
/// </para>
///
/// <para>
/// <b>A soma das contagens fecha com <c>RejectedAtEntryCount</c></b> da mesma
/// janela, porque a coluna de motivo é obrigatória. Um valor que a rota não
/// conheça chega COMO ESTÁ: omiti-lo faria a soma deixar de fechar, sem sintoma.
/// </para>
/// </summary>
public sealed record RejectionReasonResponse(string Reason, int Count);

public sealed record AgentFailureResponse(Guid AgentId, string? Provider, string? Model, int FailedCount);

public sealed record FailurePhaseResponse(string Phase, int Count);

public sealed record IndexingFailureResponse(string Outcome, string? FailurePhase, int Count);

/// <summary>
/// M32, e as DUAS populações chegam separadas porque têm causas diferentes:
/// execução aberta é task consumida e ainda rodando; task nunca consumida é task
/// publicada que instância nenhuma pegou — esta última não tem linha em
/// <c>task_executions</c>, porque a linha nasce no consumo, e só existe em
/// <c>a2a_tasks</c>.
/// </summary>
public sealed record NonTerminalTasksResponse(
    int OpenExecutionCount,
    int NeverConsumedCount,
    IReadOnlyList<string> ObservedStates);

/// <summary>M34.</summary>
public sealed record DelegationInsightsResponse(
    string Regime,
    IReadOnlyList<DelegationPairResponse> Pairs);

/// <summary>
/// O par origem → destino do lado de quem TENTOU (<c>delegation_outcomes</c>).
/// O lado "Acionado por", que lê <c>task_executions</c>, é da change B — e os
/// dois lados NÃO são espelho quando há <c>NotStarted</c> ou <c>Expired</c>.
/// </summary>
public sealed record DelegationPairResponse(
    Guid SourceAgentId,
    Guid TargetAgentId,
    string Outcome,
    int Count);
