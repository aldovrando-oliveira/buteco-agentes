using Mediator;

namespace Buteco.Api.Agents.Commands.UpdateAgent;

public sealed record UpdateAgentCommand(Guid Id, string Name, string Instructions, string Provider, string Model) : ICommand<UpdateAgentResult>;
