using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Agents.Commands.UpdateAgent;

public sealed class UpdateAgentCommandHandler(AppDbContext dbContext) : ICommandHandler<UpdateAgentCommand, AgentResponse?>
{
    public async ValueTask<AgentResponse?> Handle(UpdateAgentCommand command, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .FirstOrDefaultAsync(agent => agent.Id == command.Id, cancellationToken);

        if (agent is null)
        {
            return null;
        }

        agent.UpdateDetails(command.Name, command.Instructions);
        await dbContext.SaveChangesAsync(cancellationToken);

        return AgentResponse.FromEntity(agent);
    }
}
