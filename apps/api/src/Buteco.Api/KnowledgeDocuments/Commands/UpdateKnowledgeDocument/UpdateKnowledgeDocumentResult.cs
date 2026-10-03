using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeDocuments.Responses;

namespace Buteco.Api.KnowledgeDocuments.Commands.UpdateKnowledgeDocument;

public sealed record UpdateKnowledgeDocumentResult(
    bool Found,
    KnowledgeDocumentResponse? Document,
    KnowledgeContentRefusal? ContentRefusal,
    bool KnowledgeBaseIsSynced = false,
    bool ConcurrentWriteConflict = false)
{
    /// <summary>
    /// Duas falhas de concorrência seguidas (catalogo-base-sincronizada, D10): nada
    /// gravado, e a escrita pode ser repetida.
    /// </summary>
    public static UpdateKnowledgeDocumentResult ConcurrentWrite() => new(true, null, null, ConcurrentWriteConflict: true);

    public static UpdateKnowledgeDocumentResult NotFound() => new(false, null, null);

    /// <summary>Base sincronizada: o operador não escreve documento nela (D7 da #102).</summary>
    public static UpdateKnowledgeDocumentResult SyncedKnowledgeBase() => new(true, null, null, KnowledgeBaseIsSynced: true);

    public static UpdateKnowledgeDocumentResult Invalid(KnowledgeContentRefusal refusal) => new(true, null, refusal);

    public static UpdateKnowledgeDocumentResult Success(KnowledgeDocumentResponse document) => new(true, document, null);
}
