using Mediator;

namespace Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;

public sealed record ReplaceAgentMcpServersCommand(Guid AgentId, IReadOnlyList<ReplaceAgentMcpServersBinding> Bindings) : ICommand<ReplaceAgentMcpServersResult>;
