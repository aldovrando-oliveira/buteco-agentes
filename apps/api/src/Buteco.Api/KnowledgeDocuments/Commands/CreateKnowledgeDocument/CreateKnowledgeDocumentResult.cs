using Buteco.Api.KnowledgeDocuments.Responses;

namespace Buteco.Api.KnowledgeDocuments.Commands.CreateKnowledgeDocument;

public sealed record CreateKnowledgeDocumentResult(
    bool KnowledgeBaseFound,
    KnowledgeDocumentResponse? Document,
    Dictionary<string, string[]>? ValidationErrors)
{
    public static CreateKnowledgeDocumentResult KnowledgeBaseNotFound() => new(false, null, null);

    public static CreateKnowledgeDocumentResult Invalid(Dictionary<string, string[]> errors) => new(true, null, errors);

    public static CreateKnowledgeDocumentResult Success(KnowledgeDocumentResponse document) => new(true, document, null);
}
