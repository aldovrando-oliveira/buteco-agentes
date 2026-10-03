using Buteco.Api.KnowledgeBases.Entities;

namespace Buteco.Api.KnowledgeBases.Responses;

/// <summary>
/// Estado da sincronização de uma base sincronizada (design.md da change
/// catalogo-base-sincronizada, D2 e D13). Os códigos saem como foram gravados: o
/// texto exibido é do frontend (D1).
/// </summary>
/// <remarks>
/// <see cref="IgnoredFiles"/> é nulo enquanto nenhum ciclo terminou com sucesso, e
/// não lista vazia: lista vazia afirmaria que uma listagem aconteceu e não ignorou
/// nada (convenção 13).
/// </remarks>
public sealed record KnowledgeBaseSyncStateResponse(
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? LastFinishedAt,
    DateTimeOffset? FailingSince,
    KnowledgeBaseSyncErrorResponse? LastError,
    IReadOnlyList<KnowledgeBaseSyncIgnoredFileResponse>? IgnoredFiles)
{
    /// <summary>Nulo em base manual: "sei que não existe" (convenção 13).</summary>
    public static KnowledgeBaseSyncStateResponse? FromEntity(KnowledgeBase knowledgeBase) =>
        knowledgeBase.ContentMode == KnowledgeBaseContentMode.Synced
            ? new(
                knowledgeBase.LastSyncCompletedAt,
                knowledgeBase.LastSyncFinishedAt,
                knowledgeBase.SyncFailingSince,
                knowledgeBase.LastSyncErrorCode is null
                    ? null
                    : new KnowledgeBaseSyncErrorResponse(knowledgeBase.LastSyncErrorCode, knowledgeBase.LastSyncErrorDetail),
                knowledgeBase.SyncIgnoredFiles?
                    .Select(file => new KnowledgeBaseSyncIgnoredFileResponse(file.ExternalRef, file.Name, file.Code, file.Detail))
                    .ToList())
            : null;
}

public sealed record KnowledgeBaseSyncErrorResponse(string Code, string? Detail);

public sealed record KnowledgeBaseSyncIgnoredFileResponse(string ExternalRef, string Name, string Code, string? Detail);
