using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Queries.GetKnowledgeDocumentById;

public sealed class GetKnowledgeDocumentByIdQueryHandler(AppDbContext dbContext)
    : IQueryHandler<GetKnowledgeDocumentByIdQuery, KnowledgeDocumentResponse?>
{
    public async ValueTask<KnowledgeDocumentResponse?> Handle(
        GetKnowledgeDocumentByIdQuery query, CancellationToken cancellationToken)
    {
        // Filtra por KnowledgeBaseId E Id: documento que existe mas pertence a
        // outra base responde 404, nunca é acessível pelo caminho errado.
        return await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .Where(document => document.Id == query.Id && document.KnowledgeBaseId == query.KnowledgeBaseId)
            .Select(document => KnowledgeDocumentResponse.FromEntity(document))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
