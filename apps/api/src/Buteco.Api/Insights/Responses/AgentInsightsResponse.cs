namespace Buteco.Api.Insights.Responses;

/// <summary>
/// O agregado inteiro da aba de Insights de <b>um</b> agente.
///
/// <para>
/// <b>Tipo PRÓPRIO, e não <see cref="SystemInsightsResponse"/> reaproveitado</b>
/// (design.md, D5). O motivo não é estilo: reaproveitar o tipo do sistema
/// obrigaria a carregar <c>ByAgent</c> (M13, que não existe quando o recorte já é
/// o agente) e <c>IndexingFailures</c> (M30, que não tem fonte com agente),
/// servidos como listas vazias — e lista vazia é o texto de "medi e não achei
/// nada", que afirmaria medição onde não há fonte. É a convenção 13 furada pela
/// forma do tipo, antes de qualquer consulta rodar.
/// </para>
///
/// <para>
/// Por isso este tipo <b>não tem campo</b> para o que este escopo não sustenta. A
/// forma do record é a primeira barreira, como já é no gêmeo do sistema.
/// </para>
///
/// <para>
/// <b>O que é reusado do gêmeo, de propósito:</b>
/// <see cref="InsightsWindowResponse"/>, <see cref="DailyInsightPoint"/>,
/// <see cref="WeekdayInsightPoint"/>, <see cref="DurationStatsResponse"/>,
/// <see cref="TokenTotalsResponse"/>, <see cref="ModelTokenResponse"/>,
/// <see cref="FailurePhaseResponse"/> e <see cref="InsightsCaveats"/> — são
/// formas neutras de escopo, que significam a mesma coisa nas duas rotas.
/// Duplicá-las criaria dois lugares onde o mesmo conceito pode divergir
/// (convenção 2).
/// </para>
/// </summary>
public sealed record AgentInsightsResponse(
    Guid AgentId,
    InsightsWindowResponse Window,
    IReadOnlyDictionary<string, DateTimeOffset> Regimes,
    AgentVolumeInsightsResponse Volume,
    TemporalInsightsResponse Temporal,
    AgentTokenInsightsResponse Tokens,
    AgentPerformanceInsightsResponse Performance,
    AgentErrorInsightsResponse Errors,
    AgentDelegationInsightsResponse Delegation);

/// <summary>
/// Códigos estáveis de parcialidade <b>próprios do escopo por agente</b>. Os
/// herdados continuam vindo de <see cref="InsightsCaveats"/>, que não é alterada
/// por esta change — a rota do sistema não é tocada.
/// </summary>
public static class AgentInsightsCaveats
{
    /// <summary>
    /// <b>Tokens de embedding do agente cobrem SÓ a busca.</b> A metade de
    /// indexação pendura em <c>knowledge_indexing_attempts</c>, que tem documento
    /// e base e <b>nenhuma coluna de agente</b> — indexação é trabalho da base,
    /// não de um agente.
    ///
    /// <para>
    /// <b>E o caminho por <c>AgentKnowledgeBases</c> foi RECUSADO</b> (design.md,
    /// D6): o vínculo é de muitos para muitos, então uma indexação que aconteceu
    /// UMA vez seria contada em cada agente vinculado à base. O total por agente
    /// somaria mais que o total do sistema, e cada número afirmaria como trabalho
    /// daquele agente um trabalho que não foi dele.
    /// </para>
    ///
    /// Afeta M19 e M14.
    /// </summary>
    public const string EmbeddingCoversSearchOnly = "embedding-covers-search-only";

    /// <summary>
    /// <b>Os dois lados da delegação NÃO são espelho, e divergir é resultado
    /// correto.</b> Um conta tentativa, o outro conta execução. Duas causas
    /// independentes, e a segunda não some com período maior (design.md, D2):
    ///
    /// <list type="number">
    /// <item>resultado que não produz execução — <c>NotStarted</c> nunca cria
    /// task, e <c>Expired</c> pode ter criado uma que nunca rodou;</item>
    /// <item><b>os dois lados são situados por relógios diferentes</b> — "delega
    /// para" pela execução de ORIGEM (a janela vem do pai, porque
    /// <c>delegation_outcomes</c> não tem coluna temporal utilizável) e "acionado
    /// por" pela execução de DESTINO, que é a própria linha.</item>
    /// </list>
    ///
    /// Afeta M34.
    /// </summary>
    public const string DelegationSidesAreNotMirrors = "delegation-sides-are-not-mirrors";
}

/// <summary>M1 e M2, recortadas pelo agente. Contagens MEDIDAS — zero aqui é verdade.</summary>
public sealed record AgentVolumeInsightsResponse(
    string Regime,
    int ExecutedTaskCount,
    int ExternalOriginTaskCount,
    int DelegationOriginTaskCount);

/// <summary>
/// M11 a M17 e M19 no escopo do agente. <b>Sem <c>ByAgent</c></b>: o recorte já é
/// o agente, e um agrupamento de um elemento só com o nome de uma comparação é
/// M13 ressuscitada por descuido (design.md, D7).
/// </summary>
public sealed record AgentTokenInsightsResponse(
    string ConversationRegime,
    string EmbeddingRegime,
    TokenTotalsResponse Conversation,
    long? SearchEmbeddingInputTokens,
    IReadOnlyList<AgentProviderTokenResponse> ByProvider,
    IReadOnlyList<ModelTokenResponse> ByModel,
    TokensPerTaskResponse PerTask,
    IReadOnlyList<string> Caveats);

/// <summary>
/// M14 no escopo do agente. <b>O nome do campo carrega a verdade</b>: é
/// <c>SearchEmbeddingInputTokens</c>, e não <c>EmbeddingInputTokens</c>, porque
/// neste escopo o número não inclui a indexação. É a mesma régua que renomeou o
/// resíduo de M25 — rótulo que afirma mais do que o número sabe é convenção 13
/// furada num nome (design.md, D6).
/// </summary>
public sealed record AgentProviderTokenResponse(
    string Provider,
    long? ConversationInputTokens,
    long? ConversationOutputTokens,
    long? SearchEmbeddingInputTokens);

/// <summary>
/// M21 a M26 no escopo do agente.
///
/// <para>
/// <b><see cref="MaxDepthAtWhichAgentRan"/> NÃO é a M26 do sistema com um
/// filtro</b> — é outra pergunta com a mesma consulta (design.md, D7). No
/// sistema, a profundidade máxima é o TAMANHO da maior cadeia que o sistema
/// alcançou; aqui é a POSIÇÃO mais profunda em que este agente executou. Um
/// agente que só é chamado na ponta tem profundidade alta sem que isso diga nada
/// sobre o tamanho das cadeias dele.
/// </para>
/// </summary>
public sealed record AgentPerformanceInsightsResponse(
    string Regime,
    DurationStatsResponse TaskDuration,
    DurationStatsResponse QueueTime,
    DurationStatsResponse ProviderCallDuration,
    double? ProviderCallsPerTask,
    DurationStatsResponse NonProviderResidual,
    int? MaxDepthAtWhichAgentRan,
    IReadOnlyList<string> Caveats);

/// <summary>
/// M27 a M29 e M32 no escopo do agente. <b>Sem <c>IndexingFailures</c></b> (M30):
/// <c>knowledge_indexing_attempts</c> não tem coluna de agente, e atribuí-la pelo
/// vínculo de base contaria a mesma tentativa em cada agente vinculado.
///
/// <para>
/// <b>E sem <c>ByAgent</c></b>: M28 perde o agrupamento por agente e fica só com
/// provedor/modelo daquele agente.
/// </para>
/// </summary>
public sealed record AgentErrorInsightsResponse(
    string Regime,
    int FailedCount,
    int RejectedCount,
    IReadOnlyList<AgentModelFailureResponse> ByProviderAndModel,
    IReadOnlyList<FailurePhaseResponse> ByPhase,
    AgentNonTerminalTasksResponse NonTerminal,
    IReadOnlyList<string> Caveats);

/// <summary>M28 sem o agrupamento por agente.</summary>
public sealed record AgentModelFailureResponse(string? Provider, string? Model, int FailedCount);

/// <summary>
/// M32, as duas populações recortadas pelo agente — e as duas TÊM coluna de
/// agente: a execução aberta por <c>task_executions.AgentId</c>, e a task nunca
/// consumida por <c>a2a_tasks.agent_id</c> (design.md, D9).
/// </summary>
public sealed record AgentNonTerminalTasksResponse(
    int OpenExecutionCount,
    int NeverConsumedCount,
    IReadOnlyList<string> ObservedStates);

/// <summary>
/// M34, e é a decisão central desta rota.
///
/// <para>
/// <b>Os dois lados são conjuntos SEPARADOS, de fontes diferentes, e nenhum é
/// derivado do outro.</b> <see cref="DelegatesTo"/> é o que o agente TENTOU
/// (<c>delegation_outcomes</c> por <c>SourceAgentId</c>);
/// <see cref="TriggeredBy"/> é o que de fato RODOU nele
/// (<c>task_executions</c> com <c>Origin = 'Delegation'</c> por
/// <c>SourceAgentId</c>). São perguntas diferentes sobre a mesma relação, e
/// <b>podem mostrar números diferentes</b> — ver
/// <see cref="AgentInsightsCaveats.DelegationSidesAreNotMirrors"/>.
/// </para>
///
/// <para>
/// <b>Quem desenhar a tela não pode fazê-los parecerem espelho</b> (D16 de
/// <c>metricas-execucao-coleta</c>): se as duas seções forem desenhadas como a
/// mesma relação vista de dois lados, o operador lê a diferença como erro.
/// </para>
/// </summary>
public sealed record AgentDelegationInsightsResponse(
    string Regime,
    IReadOnlyList<DelegatesToResponse> DelegatesTo,
    IReadOnlyList<TriggeredByResponse> TriggeredBy,
    IReadOnlyList<string> Caveats);

/// <summary>
/// O que o agente TENTOU delegar, por destino e por resultado. O
/// <c>Outcome</c> é discriminado porque é ele que explica a divergência:
/// <c>NotStarted</c> não cria task alguma no destino.
/// </summary>
public sealed record DelegatesToResponse(Guid TargetAgentId, string Outcome, int Count);

/// <summary>O que de fato RODOU no agente por delegação, por agente de origem.</summary>
public sealed record TriggeredByResponse(Guid SourceAgentId, int ExecutedCount);
