using Buteco.Api.Agents.Responses;
using Mediator;

namespace Buteco.Api.Agents.Commands.CreateAgent;

public sealed record CreateAgentCommand(string Name, string Instructions) : ICommand<AgentResponse>;
