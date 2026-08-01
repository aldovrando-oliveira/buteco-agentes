using Mediator;

namespace Buteco.Api.Agents.Commands.CreateAgent;

public sealed record CreateAgentCommand(string Name, string Instructions, string Provider, string Model) : ICommand<CreateAgentResult>;
