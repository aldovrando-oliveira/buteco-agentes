namespace Buteco.Workers.EmbeddingMetrics.Entities;

/// <summary>
/// Uma <b>tentativa</b> de indexação de documento — a fonte de M30.
///
/// <para>
/// <b>Espelho</b> de <c>Buteco.Api.EmbeddingMetrics.Entities.KnowledgeIndexingAttempt</c>,
/// por cópia — os dois apps não se referenciam, mesma disciplina dos dois
/// <c>AppDbContext</c> e das entidades da etapa 1. Aqui a classe tem
/// construtor público porque <b>este</b> é o lado que escreve;
/// <c>apps/api</c> só migra. <c>EmbeddingMetricsSchemaMirrorTests</c> é o que
/// transforma a disciplina em verificação.
/// </para>
///
/// <para>
/// <b>Por que existe, se <c>knowledge_documents</c> já tem estado de
/// indexação:</b> aquelas colunas (<c>IndexingStatus</c>, <c>FailureReason</c>,
/// <c>IndexingAttempts</c>, <c>LastAttemptAt</c>) são <b>estado corrente,
/// sobrescrito a cada tentativa</b>. Uma tentativa que falhou e depois deu certo
/// não deixa rastro nenhum ali, e "falhas de indexação num período" não tem de
/// onde sair. Esta tabela é o histórico que falta.
/// </para>
///
/// <para>
/// <b>A linha existe exatamente quando a tentativa foi contada</b> (design.md,
/// D8): o descarte que acontece <b>antes</b> de contar não grava nada, e o que
/// acontece <b>depois</b> do trabalho grava com <c>Outcome = Discarded</c> —
/// porque ele consumiu tokens do gateway, e M19 tem de vê-los.
/// </para>
/// </summary>
public class KnowledgeIndexingAttempt
{
    public Guid Id { get; private set; }

    /// <summary>
    /// <b>Sem FK</b> para <c>knowledge_documents</c> nem <c>knowledge_bases</c>
    /// (design.md, D9). Não é descuido: com <c>Restrict</c>, apagar uma base
    /// passaria a falhar; com <c>Cascade</c>, o total de tokens de um período
    /// mudaria retroativamente quando alguém apagasse uma base. A métrica
    /// registra o que aconteceu, e o que aconteceu não muda porque o catálogo
    /// mudou depois.
    /// </summary>
    public Guid KnowledgeDocumentId { get; private set; }

    /// <inheritdoc cref="KnowledgeDocumentId"/>
    public Guid KnowledgeBaseId { get; private set; }

    public int ContentRevision { get; private set; }

    /// <summary>Qual tentativa foi esta — 1, 2 ou 3 de <see cref="MaxAttempts"/>.</summary>
    public int Attempt { get; private set; }

    /// <summary>
    /// Gravado na linha, e não lido de constante na hora da consulta: a política
    /// de tentativas pode mudar, e "2 de 3" tem de continuar significando o que
    /// significava no dia.
    /// </summary>
    public int MaxAttempts { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset EndedAt { get; private set; }

    /// <summary>
    /// <c>Indexed</c> | <c>RetryScheduled</c> | <c>Failed</c> | <c>Discarded</c>,
    /// gravado como <b>texto</b> (convenção 12).
    /// </summary>
    public string Outcome { get; private set; } = null!;

    /// <summary>
    /// A fase em que a indexação parou, quando não deu certo; nulo em
    /// <c>Indexed</c> e <c>Discarded</c>. <b>Fase, não texto de exceção</b>
    /// (design.md, D5).
    /// </summary>
    public string? FailurePhase { get; private set; }

    /// <summary>
    /// Quantos fragmentos ficaram gravados. Nulo quando a tentativa não indexou
    /// — nulo é "não indexou", e <c>0</c> seria "indexou zero", que é o pior
    /// caso da convenção 13 e é justamente o que o serviço recusa.
    /// </summary>
    public int? FragmentCount { get; private set; }

    private KnowledgeIndexingAttempt()
    {
    }

    public KnowledgeIndexingAttempt(
        Guid id,
        Guid knowledgeDocumentId,
        Guid knowledgeBaseId,
        int contentRevision,
        int attempt,
        int maxAttempts,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string outcome,
        string? failurePhase,
        int? fragmentCount)
    {
        // O id vem de fora, e não de um Guid.NewGuid() aqui, porque as linhas
        // filhas de embedding_calls precisam dele ANTES de a tentativa terminar
        // — elas são acumuladas durante o laço de lotes e gravadas junto com o
        // pai, no mesmo SaveChangesAsync (design.md, D7).
        Id = id;
        KnowledgeDocumentId = knowledgeDocumentId;
        KnowledgeBaseId = knowledgeBaseId;
        ContentRevision = contentRevision;
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        StartedAt = startedAt;
        EndedAt = endedAt;
        Outcome = outcome;
        FailurePhase = failurePhase;
        FragmentCount = fragmentCount;
    }
}
