using Buteco.Workers.EmbeddingMetrics.Entities;

namespace Buteco.Workers.EmbeddingMetrics;

/// <summary>
/// O acumulador, em memória, das métricas de <b>uma</b> tentativa de indexação:
/// as chamadas ao gateway e a fase em que o código está.
/// </summary>
/// <remarks>
/// <para>
/// <b>NÃO é um <c>AsyncLocal</c>, e a ausência é decisão</b> (design.md, D4). A
/// etapa 1 precisou de um porque quem mede a requisição de chat
/// (<c>LlmCallDurationChatClient</c>) é compartilhado por <c>(provider, model)</c>
/// durante a vida do processo e não sabe de qual task é a chamada, e o caminho
/// até lá atravessa tools e a estratégia de compactação. Aqui o laço de lotes é
/// método privado da <b>mesma classe</b> que é a unidade de trabalho, então o
/// acumulador é um parâmetro. Maquinaria estática para alcançar um método
/// privado da própria classe seria maquinaria sem motivo (convenção 2).
/// </para>
///
/// <para>
/// <b>Sem <c>lock</c></b>, pelo mesmo motivo: os lotes são gerados
/// <b>sequencialmente</b> (spec "Geração de embedding é loteada"), num fluxo só.
/// O <c>lock</c> de <c>ExecutionMetricsScope</c> existe porque lá o
/// <c>FunctionInvokingChatClient</c> pode invocar duas tools em paralelo — não é
/// o caso aqui.
/// </para>
/// </remarks>
public sealed class KnowledgeIndexingAttemptContext(Guid knowledgeDocumentId, int contentRevision, int attempt, int maxAttempts, DateTimeOffset startedAt)
{
    private readonly List<EmbeddingCall> _calls = [];

    /// <summary>
    /// Gerado na construção, e não na gravação, porque as linhas filhas precisam
    /// dele <b>durante</b> o laço de lotes — elas são montadas ali e gravadas
    /// junto com o pai, no mesmo <c>SaveChangesAsync</c> (D7).
    /// </summary>
    public Guid AttemptId { get; } = Guid.NewGuid();

    public Guid KnowledgeDocumentId { get; } = knowledgeDocumentId;

    public int ContentRevision { get; } = contentRevision;

    public int Attempt { get; } = attempt;

    public int MaxAttempts { get; } = maxAttempts;

    public DateTimeOffset StartedAt { get; } = startedAt;

    /// <summary>
    /// A base do documento, conhecida só depois da leitura. Fica aqui porque as
    /// linhas de chamada a carregam.
    /// </summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>
    /// <b>Só há linha de tentativa quando a tentativa foi contada</b> (D8). O
    /// descarte que acontece antes de <c>MarkAttemptStartedAsync</c> não grava
    /// nada — é o que mantém a contagem de linhas reconciliável com
    /// <c>IndexingAttempts</c> do documento.
    /// </summary>
    public bool Counted { get; private set; }

    /// <summary>
    /// Onde o código está. Lida na gravação quando a tentativa não indexou —
    /// <b>fase, nunca texto de exceção</b> (D5).
    /// </summary>
    public string? Phase { get; set; }

    /// <summary>Quantos fragmentos foram gravados, quando foram.</summary>
    public int? FragmentCount { get; set; }

    public IReadOnlyList<EmbeddingCall> Calls => _calls;

    public void MarkCounted() => Counted = true;

    public void Record(EmbeddingCall call) => _calls.Add(call);
}
