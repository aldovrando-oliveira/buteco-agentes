namespace Buteco.Api.AgentMcpBindings.Requests;

public record ReplaceAgentMcpServersRequest(IReadOnlyList<AgentMcpServerBindingRequest>? McpServers);
