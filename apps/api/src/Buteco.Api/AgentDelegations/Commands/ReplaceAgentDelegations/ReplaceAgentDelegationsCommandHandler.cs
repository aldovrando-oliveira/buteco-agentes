using Buteco.Api.A2A;
using Buteco.Api.AgentDelegations.Entities;
using Buteco.Api.AgentKnowledgeBindings;
using Buteco.Api.AgentMcpBindings;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.AgentDelegations.Commands.ReplaceAgentDelegations;

public sealed class ReplaceAgentDelegationsCommandHandler(AppDbContext dbContext,
    IOptions<PublicUrlOptions> publicUrlOptions) : ICommandHandler<ReplaceAgentDelegationsCommand, ReplaceAgentDelegationsResult>
{
    public async ValueTask<ReplaceAgentDelegationsResult> Handle(ReplaceAgentDelegationsCommand command, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .FirstOrDefaultAsync(agent => agent.Id == command.AgentId, cancellationToken);

        if (agent is null)
        {
            return ReplaceAgentDelegationsResult.AgentNotFound();
        }

        var requestedTargetIds = command.TargetAgentIds.Distinct().ToList();

        // Auto-delegação (Decision 2 do design.md): ciclo de profundidade 1,
        // rejeitado explicitamente aqui em vez de deixar implícito sob o
        // guarda-chuva geral de controle de ciclo (Decision 3, fora de
        // escopo desta camada).
        if (requestedTargetIds.Contains(command.AgentId))
        {
            return ReplaceAgentDelegationsResult.SelfDelegation();
        }

        var existingTargetIds = await dbContext.Agents
            .Where(targetAgent => requestedTargetIds.Contains(targetAgent.Id))
            .Select(targetAgent => targetAgent.Id)
            .ToListAsync(cancellationToken);

        var invalidTargetIds = requestedTargetIds.Except(existingTargetIds).ToList();
        if (invalidTargetIds.Count > 0)
        {
            return ReplaceAgentDelegationsResult.InvalidIds(invalidTargetIds);
        }

        var currentDelegations = await dbContext.AgentDelegations
            .Where(delegation => delegation.SourceAgentId == command.AgentId)
            .ToListAsync(cancellationToken);
        dbContext.AgentDelegations.RemoveRange(currentDelegations);

        foreach (var targetAgentId in requestedTargetIds)
        {
            dbContext.AgentDelegations.Add(new AgentDelegation(command.AgentId, targetAgentId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var mcpServers = await AgentMcpServerLookup.GetLinkedMcpServersAsync(dbContext, agent.Id, cancellationToken);
        var delegatesTo = await AgentDelegationLookup.GetDelegateTargetsAsync(dbContext, agent.Id, cancellationToken);
        var knowledgeBases = await AgentKnowledgeBaseLookup.GetLinkedKnowledgeBasesAsync(dbContext, agent.Id, cancellationToken);

        return ReplaceAgentDelegationsResult.Success(AgentResponse.FromEntity(agent, mcpServers, delegatesTo, knowledgeBases, AgentA2AAddressBuilder.Build(publicUrlOptions.Value, agent.Id)));
    }
}
