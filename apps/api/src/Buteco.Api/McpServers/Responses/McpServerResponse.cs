using Buteco.Api.McpServers.Entities;

namespace Buteco.Api.McpServers.Responses;

/// <summary>
/// Nunca inclui a credencial (nem em texto claro, nem criptografada) — ver
/// Requirement de cadastro/consulta/listagem em specs/mcp-server-catalog.
/// </summary>
public sealed record McpServerResponse(
    Guid Id,
    string Name,
    string Description,
    string Url,
    McpServerAuthType AuthType,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static McpServerResponse FromEntity(McpServer mcpServer) => new(
        mcpServer.Id,
        mcpServer.Name,
        mcpServer.Description,
        mcpServer.Url,
        mcpServer.AuthType,
        mcpServer.IsActive,
        mcpServer.CreatedAt,
        mcpServer.UpdatedAt);
}
