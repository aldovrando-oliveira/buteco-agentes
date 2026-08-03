namespace Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;

public sealed record ReplaceAgentMcpServersBinding(Guid McpServerId, IReadOnlyList<string> AllowedTools);
