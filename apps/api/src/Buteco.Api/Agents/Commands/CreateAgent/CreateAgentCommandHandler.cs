using Buteco.Api.A2A;
using Buteco.Api.Agents.Entities;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Mediator;

namespace Buteco.Api.Agents.Commands.CreateAgent;

public sealed class CreateAgentCommandHandler(
    AppDbContext dbContext,
    IAgentA2AServerRegistry registry) : ICommandHandler<CreateAgentCommand, AgentResponse>
{
    public async ValueTask<AgentResponse> Handle(CreateAgentCommand command, CancellationToken cancellationToken)
    {
        var agent = new Agent(command.Name, command.Instructions);

        dbContext.Agents.Add(agent);
        await dbContext.SaveChangesAsync(cancellationToken);

        // A rota A2A do agente (/agents/{id}/a2a) resolve o A2AServer sob demanda a
        // partir deste registry; pré-populamos aqui para a primeira chamada já
        // responder sem precisar de uma consulta extra ao banco (ver RoutingA2ARequestHandler).
        registry.Register(agent.Id);

        return AgentResponse.FromEntity(agent);
    }
}
