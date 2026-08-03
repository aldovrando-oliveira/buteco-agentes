namespace Buteco.Api.AgentMcpBindings.Requests;

public sealed record AgentMcpServerBindingRequest(Guid McpServerId, IReadOnlyList<string> AllowedTools);
