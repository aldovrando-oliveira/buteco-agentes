namespace Buteco.Api.KnowledgeDocuments.Entities;

/// <summary>
/// Um evento do histórico de documentos de uma base (design.md da change
/// historico-documentos-base). Gravado por <c>apps/api</c> no mesmo
/// <c>SaveChangesAsync</c> da escrita do documento (D2): só existe evento de
/// escrita que de fato aconteceu.
///
/// <para>
/// <b>Sem FK para o documento</b> (D1): o evento de exclusão precisa sobreviver
/// a ele. Por isso <see cref="DocumentId"/> é coluna solta e
/// <see cref="DocumentTitle"/> é snapshot. A FK é só para a base, em cascata
/// (D3): o histórico é a auditoria da base, e sem ela não há onde lê-lo.
/// </para>
///
/// <para>
/// Os construtores são as fábricas abaixo, e só elas: cada uma monta o formato
/// que a <c>CHECK</c> do banco exige (D5) — detalhe nulo em
/// <see cref="KnowledgeDocumentEventType.Created"/> e
/// <see cref="KnowledgeDocumentEventType.Deleted"/>, não nulo e com pelo menos
/// uma mudança em <see cref="KnowledgeDocumentEventType.Updated"/>.
/// </para>
/// </summary>
public class KnowledgeDocumentEvent
{
    public Guid Id { get; private set; }

    public Guid KnowledgeBaseId { get; private set; }

    public Guid DocumentId { get; private set; }

    /// <summary>
    /// Título depois da escrita; na exclusão, o título que o documento tinha.
    /// </summary>
    public string DocumentTitle { get; private set; } = null!;

    public KnowledgeDocumentEventType Type { get; private set; }

    /// <summary>
    /// Só em <see cref="KnowledgeDocumentEventType.Updated"/>. Nulo, e não
    /// <c>false</c>, nos outros tipos: <c>false</c> numa inclusão afirmaria "o
    /// conteúdo não mudou", que o sistema não sabe dizer (D5).
    /// </summary>
    public bool? ContentChanged { get; private set; }

    /// <inheritdoc cref="ContentChanged"/>
    public bool? TitleChanged { get; private set; }

    /// <summary>
    /// Subject do token que fez a escrita, gravado como veio (D6). O rótulo de
    /// apresentação ("operador", "sincronização") é do frontend.
    /// </summary>
    public string Author { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    private KnowledgeDocumentEvent()
    {
    }

    private KnowledgeDocumentEvent(
        KnowledgeDocument document,
        KnowledgeDocumentEventType type,
        bool? contentChanged,
        bool? titleChanged,
        string author)
    {
        Id = Guid.NewGuid();
        KnowledgeBaseId = document.KnowledgeBaseId;
        DocumentId = document.Id;
        DocumentTitle = document.Title;
        Type = type;
        ContentChanged = contentChanged;
        TitleChanged = titleChanged;
        Author = author;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    public static KnowledgeDocumentEvent Created(KnowledgeDocument document, string author) =>
        new(document, KnowledgeDocumentEventType.Created, null, null, author);

    /// <summary>
    /// Só é chamada quando <paramref name="outcome"/> registra mudança de texto
    /// ou de título; chamá-la sem mudança é defeito do chamador, e falha aqui
    /// antes de a <c>CHECK</c> do banco recusar a linha.
    /// </summary>
    public static KnowledgeDocumentEvent Updated(
        KnowledgeDocument document, KnowledgeDocumentUpdateOutcome outcome, string author)
    {
        if (!outcome.HasDocumentChange)
        {
            throw new InvalidOperationException(
                "Evento Updated sem mudança de texto nem de título: o chamador deveria ter verificado HasDocumentChange.");
        }

        return new(document, KnowledgeDocumentEventType.Updated, outcome.ContentChanged, outcome.TitleChanged, author);
    }

    public static KnowledgeDocumentEvent Deleted(KnowledgeDocument document, string author) =>
        new(document, KnowledgeDocumentEventType.Deleted, null, null, author);
}
