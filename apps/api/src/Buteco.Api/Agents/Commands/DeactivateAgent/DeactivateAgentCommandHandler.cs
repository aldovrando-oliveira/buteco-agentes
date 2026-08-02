using Buteco.Api.AgentMcpBindings;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Agents.Commands.DeactivateAgent;

public sealed class DeactivateAgentCommandHandler(AppDbContext dbContext) : ICommandHandler<DeactivateAgentCommand, AgentResponse?>
{
    public async ValueTask<AgentResponse?> Handle(DeactivateAgentCommand command, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .FirstOrDefaultAsync(agent => agent.Id == command.Id, cancellationToken);

        if (agent is null)
        {
            return null;
        }

        agent.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);

        var mcpServers = await AgentMcpServerLookup.GetLinkedMcpServersAsync(dbContext, agent.Id, cancellationToken);

        return AgentResponse.FromEntity(agent, mcpServers);
    }
}
