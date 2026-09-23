using Buteco.Workers.EmbeddingMetrics;
namespace Buteco.Workers.EmbeddingMetrics.Entities;

/// <summary>
/// Uma chamada ao gateway de embedding — a fonte de M19. <b>Espelho</b> de
/// <c>Buteco.Api.EmbeddingMetrics.Entities.EmbeddingCall</c>, por cópia; este é
/// o lado que escreve (ver <see cref="KnowledgeIndexingAttempt"/>).
///
/// <para>
/// <b>O grão é a chamada, e na indexação isso quer dizer o LOTE</b> (design.md,
/// D3): a indexação é loteada em <c>Chunk(BatchSize)</c>, então um documento
/// grande produz N linhas. É esse grão que torna o <c>502</c> de um lote
/// específico consultável — foi a ausência dele que obrigou a reproduzir aquele
/// erro à mão em 20/09/2026.
/// </para>
///
/// <para>
/// <b>Tabela própria, e não <c>provider_calls</c></b> (D1): M11 (tokens de
/// conversa) e M19 são visões separadas por decisão do dono, então pôr as duas
/// na mesma tabela obrigaria <b>toda</b> consulta de M11 a M17 a filtrar por
/// <c>Purpose</c> — e quem esquecesse o filtro não receberia erro, receberia um
/// número maior e plausível, com o modelo de embedding em primeiro no ranking.
/// As duas se somam <b>só</b> no nível do provedor (M14), por <c>union</c>.
/// </para>
/// </summary>
public class EmbeddingCall
{
    public Guid Id { get; private set; }

    /// <summary>
    /// <c>Indexing</c> | <c>Search</c>, como <b>texto</b>. São dois consumidores
    /// do mesmo gateway com perguntas diferentes — indexação é custo de
    /// cadastro, busca é custo por conversa —, e é esta coluna que determina
    /// qual dos dois pais está preenchido.
    /// </summary>
    public string Purpose { get; private set; } = null!;

    /// <summary>
    /// Preenchido <b>se e só se</b> <see cref="Purpose"/> for <c>Indexing</c>.
    /// FK para <c>knowledge_indexing_attempts</c>, <c>Restrict</c> — vale por
    /// construção, porque pai e filhas entram no mesmo <c>SaveChangesAsync</c>
    /// (D7).
    /// </summary>
    public Guid? KnowledgeIndexingAttemptId { get; private set; }

    /// <summary>
    /// Preenchido <b>se e só se</b> <see cref="Purpose"/> for <c>Search</c> — a
    /// busca roda dentro do turno do agente, e é o único ponto em que embedding
    /// e execução de task se encontram. FK para <c>task_executions</c>,
    /// <c>Restrict</c>; vale por construção porque a linha é gravada no
    /// fechamento da execução, quando o pai já existe.
    /// </summary>
    public string? TaskId { get; private set; }

    /// <summary>
    /// A única coluna que as <b>duas</b> finalidades têm, e por isso duplicada
    /// aqui em vez de alcançada por junção: M19 por base é consulta de primeira
    /// ordem. <b>Sem FK</b>, pelo mesmo motivo de
    /// <see cref="KnowledgeIndexingAttempt.KnowledgeDocumentId"/>.
    /// </summary>
    public Guid KnowledgeBaseId { get; private set; }

    /// <summary>Snapshot do que estava configurado na hora, sem join com configuração corrente (D9).</summary>
    public string Provider { get; private set; } = null!;

    /// <inheritdoc cref="Provider"/>
    public string Model { get; private set; } = null!;

    /// <summary>
    /// A dimensão <b>declarada</b>. Está na linha porque o gateway foi medido
    /// <b>aceitando</b> o parâmetro <c>dimensions</c> e ignorando-o: sem ela,
    /// uma troca de modelo não deixaria rastro do que se pediu.
    /// </summary>
    public int Dimensions { get; private set; }

    /// <summary>
    /// Quantas entradas foram nesta chamada — o tamanho do lote na indexação,
    /// sempre <c>1</c> na busca. É o que torna verificável que a soma dos lotes
    /// fecha com o número de fragmentos do documento.
    /// </summary>
    public int InputCount { get; private set; }

    public double DurationMs { get; private set; }

    /// <summary>
    /// Nulo = o provedor não reportou; <c>0</c> = o provedor reportou zero.
    /// <b>Nunca normalizado</b> (convenção 13).
    ///
    /// <para>
    /// <b>Não há coluna de tokens de saída</b>, e a ausência é decisão (D10):
    /// embedding não produz saída, e uma coluna sempre nula convida a somá-la.
    /// </para>
    /// </summary>
    public long? InputTokens { get; private set; }

    public bool Failed { get; private set; }

    /// <summary>
    /// Só quando o SDK o expõe <b>tipado</b>; nulo = não se sabe, nunca um
    /// código inventado. No caminho <c>openai</c> a exceção é
    /// <c>ClientResultException</c>, que deriva de <c>Exception</c> e não de
    /// <c>HttpRequestException</c> — verificado por execução, e já coberto pelo
    /// <c>HttpStatusOf</c> da etapa 1 (D6).
    /// </summary>
    public int? HttpStatus { get; private set; }

    private EmbeddingCall()
    {
    }

    /// <summary>
    /// A linha de uma chamada da <b>indexação</b>: pai é a tentativa,
    /// <see cref="TaskId"/> fica nulo.
    /// </summary>
    public static EmbeddingCall ForIndexing(
        Guid attemptId,
        Guid knowledgeBaseId,
        string provider,
        string model,
        int dimensions,
        EmbeddingCallMeasurement measurement) =>
        new()
        {
            Id = Guid.NewGuid(),
            Purpose = EmbeddingMetricsValues.Purpose.Indexing,
            KnowledgeIndexingAttemptId = attemptId,
            TaskId = null,
            KnowledgeBaseId = knowledgeBaseId,
            Provider = provider,
            Model = model,
            Dimensions = dimensions,
            InputCount = measurement.InputCount,
            DurationMs = measurement.DurationMs,
            InputTokens = measurement.InputTokens,
            Failed = measurement.Failed,
            HttpStatus = measurement.HttpStatus,
        };

    /// <summary>
    /// A linha de uma chamada da <b>busca</b>: pai é a execução da task,
    /// <see cref="KnowledgeIndexingAttemptId"/> fica nulo.
    /// </summary>
    public static EmbeddingCall ForSearch(
        string taskId,
        Guid knowledgeBaseId,
        string provider,
        string model,
        int dimensions,
        EmbeddingCallMeasurement measurement) =>
        new()
        {
            Id = Guid.NewGuid(),
            Purpose = EmbeddingMetricsValues.Purpose.Search,
            KnowledgeIndexingAttemptId = null,
            TaskId = taskId,
            KnowledgeBaseId = knowledgeBaseId,
            Provider = provider,
            Model = model,
            Dimensions = dimensions,
            InputCount = measurement.InputCount,
            DurationMs = measurement.DurationMs,
            InputTokens = measurement.InputTokens,
            Failed = measurement.Failed,
            HttpStatus = measurement.HttpStatus,
        };
}
