namespace Buteco.Api.KnowledgeSync.Requests;

/// <summary>
/// O resultado de um ciclo (D2, D8). <c>Outcome</c> é <c>Succeeded</c> ou
/// <c>Failed</c>, e os campos do outro desfecho são recusados, não ignorados: é
/// contrato entre apps, e ignorar esconderia um defeito do conector.
/// </summary>
public record RecordSyncResultRequest(
    string? Outcome,
    string? FolderName,
    string? FolderUrl,
    IReadOnlyList<SyncIgnoredFileRequest?>? IgnoredFiles,
    SyncErrorRequest? Error);

public record SyncIgnoredFileRequest(string? ExternalRef, string? Name, string? Code, string? Detail);

public record SyncErrorRequest(string? Code, string? Detail);
