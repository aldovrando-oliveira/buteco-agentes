namespace Buteco.Api.KnowledgeDocuments.Responses;

/// <summary>
/// Uma página do histórico (design.md da change historico-documentos-base, D7).
/// <c>NextCursor</c> é nulo exatamente quando não há página seguinte.
/// </summary>
public sealed record KnowledgeDocumentEventPageResponse(
    IReadOnlyList<KnowledgeDocumentEventResponse> Items,
    string? NextCursor);
