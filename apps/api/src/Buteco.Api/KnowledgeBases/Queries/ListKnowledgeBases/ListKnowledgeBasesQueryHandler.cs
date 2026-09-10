using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeBases.Queries.ListKnowledgeBases;

/// <summary>
/// Inclui bases inativas — desativar impede o uso pelo agente, não a
/// visibilidade no catálogo (mesmo comportamento de <c>ListMcpServers</c>).
/// Lista vazia é resposta normal, nunca 404.
/// </summary>
public sealed class ListKnowledgeBasesQueryHandler(AppDbContext dbContext)
    : IQueryHandler<ListKnowledgeBasesQuery, IReadOnlyList<KnowledgeBaseResponse>>
{
    public async ValueTask<IReadOnlyList<KnowledgeBaseResponse>> Handle(ListKnowledgeBasesQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.KnowledgeBases
            .AsNoTracking()
            // Desempate estável: CreatedAt não é único — é atribuído no
            // construtor da entidade e dois registros podem compartilhar o
            // instante —, então ordenar só por ele deixa a ordem entre
            // empatados a cargo do plano do Postgres (api-response-ordering).
            .OrderBy(knowledgeBase => knowledgeBase.CreatedAt)
            .ThenBy(knowledgeBase => knowledgeBase.Id)
            .Select(knowledgeBase => KnowledgeBaseResponse.FromEntity(knowledgeBase))
            .ToListAsync(cancellationToken);
    }
}
