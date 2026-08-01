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

        agent.UpdateDetails(command.Name, command.Instructions, command.Provider, command.Model);
        await dbContext.SaveChangesAsync(cancellationToken);

        return UpdateAgentResult.Success(AgentResponse.FromEntity(agent));
    }
}
