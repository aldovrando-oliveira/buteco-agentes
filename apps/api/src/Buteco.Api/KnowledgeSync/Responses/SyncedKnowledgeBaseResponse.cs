namespace Buteco.Api.KnowledgeSync.Responses;

/// <summary>Uma base sincronizada, do jeito que o conector precisa (D8).</summary>
public sealed record SyncedKnowledgeBaseResponse(
    Guid Id,
    string Provider,
    string FolderId,
    string FolderName,
    string FolderUrl,
    bool IsActive);
