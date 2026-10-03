using Buteco.Api.KnowledgeBases.Entities;

namespace Buteco.Api.KnowledgeBases.Responses;

public sealed record KnowledgeBaseResponse(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    KnowledgeBaseContentMode ContentMode,
    KnowledgeBaseSyncSourceResponse? SyncSource,
    KnowledgeBaseSyncStateResponse? SyncState)
{
    public static KnowledgeBaseResponse FromEntity(KnowledgeBase knowledgeBase) => new(
        knowledgeBase.Id,
        knowledgeBase.Name,
        knowledgeBase.Description,
        knowledgeBase.IsActive,
        knowledgeBase.CreatedAt,
        knowledgeBase.UpdatedAt,
        knowledgeBase.ContentMode,
        KnowledgeBaseSyncSourceResponse.FromEntity(knowledgeBase),
        KnowledgeBaseSyncStateResponse.FromEntity(knowledgeBase));
}
