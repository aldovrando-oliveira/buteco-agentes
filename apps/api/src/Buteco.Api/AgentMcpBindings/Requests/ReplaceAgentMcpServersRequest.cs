namespace Buteco.Api.AgentMcpBindings.Requests;

public record ReplaceAgentMcpServersRequest(IReadOnlyList<Guid>? McpServerIds);
