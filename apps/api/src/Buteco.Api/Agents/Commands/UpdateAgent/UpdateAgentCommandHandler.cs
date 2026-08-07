using Buteco.Api.AgentDelegations;
using Buteco.Api.AgentMcpBindings;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Providers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Agents.Commands.UpdateAgent;

public sealed class UpdateAgentCommandHandler(
    AppDbContext dbContext,
    ProviderCatalogService providerCatalogService) : ICommandHandler<UpdateAgentCommand, UpdateAgentResult>
{
    public async ValueTask<UpdateAgentResult> Handle(UpdateAgentCommand command, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .FirstOrDefaultAsync(agent => agent.Id == command.Id, cancellationToken);

        if (agent is null)
        {
            return UpdateAgentResult.NotFound();
        }

        var validation = providerCatalogService.Validate(command.Provider, command.Model);
        if (validation != ProviderValidationOutcome.Valid)
        {
            return UpdateAgentResult.ValidationFailed(validation);
        }

        agent.UpdateDetails(command.Name, command.Instructions, command.Provider, command.Model, command.Description, command.Skills);
        await dbContext.SaveChangesAsync(cancellationToken);

        var mcpServers = await AgentMcpServerLookup.GetLinkedMcpServersAsync(dbContext, agent.Id, cancellationToken);
        var delegatesTo = await AgentDelegationLookup.GetDelegateTargetsAsync(dbContext, agent.Id, cancellationToken);

        return UpdateAgentResult.Success(AgentResponse.FromEntity(agent, mcpServers, delegatesTo));
    }
}
