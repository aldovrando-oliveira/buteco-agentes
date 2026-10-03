using Buteco.Api.KnowledgeBases.Entities;
using Mediator;

namespace Buteco.Api.KnowledgeSync.Commands.RecordSyncResult;

/// <summary>
/// Resultado de um ciclo, já validado no endpoint. <see cref="Succeeded"/> carrega
/// nome, URL e ignorados; o contrário carrega o erro.
/// </summary>
public sealed record RecordSyncResultCommand(
    Guid KnowledgeBaseId,
    bool Succeeded,
    string? FolderName,
    string? FolderUrl,
    IReadOnlyList<KnowledgeBaseSyncIgnoredFile>? IgnoredFiles,
    string? ErrorCode,
    string? ErrorDetail) : ICommand<RecordSyncResultResult>;
