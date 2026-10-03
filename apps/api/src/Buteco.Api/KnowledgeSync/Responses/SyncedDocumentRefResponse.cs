namespace Buteco.Api.KnowledgeSync.Responses;

/// <summary>
/// Referência e versão de um documento sincronizado, sem o texto (D9): é o que o
/// conector compara com a pasta para decidir o que baixar e o que excluir.
/// </summary>
public sealed record SyncedDocumentRefResponse(string ExternalRef, string ExternalVersion, Guid DocumentId);
