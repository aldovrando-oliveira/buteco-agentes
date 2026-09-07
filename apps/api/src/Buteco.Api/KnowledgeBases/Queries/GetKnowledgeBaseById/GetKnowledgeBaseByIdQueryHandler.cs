using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeBases.Queries.GetKnowledgeBaseById;

/// <summary>
/// Base inativa responde normalmente com <c>isActive: false</c> — nunca tratada
/// como não encontrada.
/// </summary>
public sealed class GetKnowledgeBaseByIdQueryHandler(AppDbContext dbContext)
    : IQueryHandler<GetKnowledgeBaseByIdQuery, KnowledgeBaseResponse?>
{
    public async ValueTask<KnowledgeBaseResponse?> Handle(GetKnowledgeBaseByIdQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.KnowledgeBases
            .AsNoTracking()
            .Where(knowledgeBase => knowledgeBase.Id == query.Id)
            .Select(knowledgeBase => KnowledgeBaseResponse.FromEntity(knowledgeBase))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
