namespace Buteco.Api.KnowledgeSync.Requests;

/// <summary>
/// <c>ExternalRef</c> no corpo, e não no caminho (D8): é string opaca de um conjunto
/// aberto de provedores, e o id do próximo pode ter <c>/</c>.
/// </summary>
public record UpsertSyncedDocumentRequest(
    string? ExternalRef,
    string? ExternalVersion,
    string? Title,
    string? SourceType,
    string? Content);
