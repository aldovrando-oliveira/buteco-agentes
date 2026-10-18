namespace Buteco.Api.McpServers.Requests;

public record TestMcpServerConfigRequest(string? Url, string? AuthType, string? Credential, string? AuthHeaderName = null);
