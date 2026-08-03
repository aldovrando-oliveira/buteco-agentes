namespace Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;

public sealed record InvalidMcpServerTool(Guid McpServerId, IReadOnlyList<string> RejectedTools);
