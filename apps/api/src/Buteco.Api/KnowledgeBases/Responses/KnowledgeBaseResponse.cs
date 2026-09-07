using Buteco.Api.KnowledgeBases.Entities;

namespace Buteco.Api.KnowledgeBases.Responses;

public sealed record KnowledgeBaseResponse(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static KnowledgeBaseResponse FromEntity(KnowledgeBase knowledgeBase) => new(
        knowledgeBase.Id,
        knowledgeBase.Name,
        knowledgeBase.Description,
        knowledgeBase.IsActive,
        knowledgeBase.CreatedAt,
        knowledgeBase.UpdatedAt);
}
