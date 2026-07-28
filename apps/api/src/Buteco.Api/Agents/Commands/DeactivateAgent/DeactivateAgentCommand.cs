using Buteco.Api.Agents.Responses;
using Mediator;

namespace Buteco.Api.Agents.Commands.DeactivateAgent;

public sealed record DeactivateAgentCommand(Guid Id) : ICommand<AgentResponse?>;
