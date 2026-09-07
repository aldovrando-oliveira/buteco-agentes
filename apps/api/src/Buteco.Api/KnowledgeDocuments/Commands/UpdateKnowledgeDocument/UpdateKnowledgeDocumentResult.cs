using Buteco.Api.KnowledgeDocuments.Responses;

namespace Buteco.Api.KnowledgeDocuments.Commands.UpdateKnowledgeDocument;

public sealed record UpdateKnowledgeDocumentResult(
    bool Found,
    KnowledgeDocumentResponse? Document,
    Dictionary<string, string[]>? ValidationErrors)
{
    public static UpdateKnowledgeDocumentResult NotFound() => new(false, null, null);

    public static UpdateKnowledgeDocumentResult Invalid(Dictionary<string, string[]> errors) => new(true, null, errors);

    public static UpdateKnowledgeDocumentResult Success(KnowledgeDocumentResponse document) => new(true, document, null);
}
