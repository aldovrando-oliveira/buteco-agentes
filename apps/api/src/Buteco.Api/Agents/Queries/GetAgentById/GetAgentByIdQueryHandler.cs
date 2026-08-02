using Buteco.Api.AgentMcpBindings;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Agents.Queries.GetAgentById;

public sealed class GetAgentByIdQueryHandler(AppDbContext dbContext) : IQueryHandler<GetAgentByIdQuery, AgentResponse?>
{
    public async ValueTask<AgentResponse?> Handle(GetAgentByIdQuery query, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .AsNoTracking()
            .FirstOrDefaultAsync(agent => agent.Id == query.Id, cancellationToken);

        if (agent is null)
        {
            return null;
        }

        var mcpServers = await AgentMcpServerLookup.GetLinkedMcpServersAsync(dbContext, agent.Id, cancellationToken);

        return AgentResponse.FromEntity(agent, mcpServers);
    }
}
