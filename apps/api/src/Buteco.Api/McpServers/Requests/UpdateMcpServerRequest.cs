namespace Buteco.Api.McpServers.Requests;

public record UpdateMcpServerRequest(string? Name, string? Description, string? Url, string? AuthType, string? Credential);
