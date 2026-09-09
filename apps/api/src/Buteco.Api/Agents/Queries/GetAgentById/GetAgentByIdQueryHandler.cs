using Buteco.Api.A2A;
using Buteco.Api.AgentDelegations;
using Buteco.Api.AgentKnowledgeBindings;
using Buteco.Api.AgentMcpBindings;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Agents.Queries.GetAgentById;

public sealed class GetAgentByIdQueryHandler(AppDbContext dbContext,
    IOptions<PublicUrlOptions> publicUrlOptions) : IQueryHandler<GetAgentByIdQuery, AgentResponse?>
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
        var delegatesTo = await AgentDelegationLookup.GetDelegateTargetsAsync(dbContext, agent.Id, cancellationToken);
        var knowledgeBases = await AgentKnowledgeBaseLookup.GetLinkedKnowledgeBasesAsync(dbContext, agent.Id, cancellationToken);

        return AgentResponse.FromEntity(agent, mcpServers, delegatesTo, knowledgeBases, AgentA2AAddressBuilder.Build(publicUrlOptions.Value, agent.Id));
    }
}
