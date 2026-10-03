using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeDocuments.Responses;

namespace Buteco.Api.KnowledgeDocuments.Commands.CreateKnowledgeDocument;

public sealed record CreateKnowledgeDocumentResult(
    bool KnowledgeBaseFound,
    KnowledgeDocumentResponse? Document,
    KnowledgeContentRefusal? ContentRefusal,
    bool KnowledgeBaseIsSynced = false)
{
    public static CreateKnowledgeDocumentResult KnowledgeBaseNotFound() => new(false, null, null);

    /// <summary>Base sincronizada: o operador não escreve documento nela (D7 da #102).</summary>
    public static CreateKnowledgeDocumentResult SyncedKnowledgeBase() => new(true, null, null, KnowledgeBaseIsSynced: true);

    public static CreateKnowledgeDocumentResult Invalid(KnowledgeContentRefusal refusal) => new(true, null, refusal);

    public static CreateKnowledgeDocumentResult Success(KnowledgeDocumentResponse document) => new(true, document, null);
}
