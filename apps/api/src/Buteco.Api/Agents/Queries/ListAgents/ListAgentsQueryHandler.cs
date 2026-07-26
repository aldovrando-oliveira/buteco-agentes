using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Agents.Queries.ListAgents;

public sealed class ListAgentsQueryHandler(AppDbContext dbContext) : IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentResponse>>
{
    public async ValueTask<IReadOnlyList<AgentResponse>> Handle(ListAgentsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.Agents
            .AsNoTracking()
            .OrderBy(agent => agent.CreatedAt)
            .Select(agent => AgentResponse.FromEntity(agent))
            .ToListAsync(cancellationToken);
    }
}
