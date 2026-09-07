namespace Buteco.Api.KnowledgeDocuments.Requests;

public record UpdateKnowledgeDocumentRequest(string? Title, string? SourceType, string? Content);
