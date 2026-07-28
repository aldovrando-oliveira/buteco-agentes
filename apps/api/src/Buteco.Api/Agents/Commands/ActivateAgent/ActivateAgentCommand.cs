using Buteco.Api.Agents.Responses;
using Mediator;

namespace Buteco.Api.Agents.Commands.ActivateAgent;

public sealed record ActivateAgentCommand(Guid Id) : ICommand<AgentResponse?>;
