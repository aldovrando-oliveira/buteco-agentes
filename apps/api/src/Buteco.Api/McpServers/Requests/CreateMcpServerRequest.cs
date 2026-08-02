namespace Buteco.Api.McpServers.Requests;

public record CreateMcpServerRequest(string? Name, string? Description, string? Url, string? AuthType, string? Credential);
