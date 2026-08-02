using Buteco.Api.McpServers.Entities;

namespace Buteco.Api.McpServers.Responses;

/// <summary>
/// Nível de detalhe usado em <c>AgentResponse.McpServers</c> (Decision 5 do
/// design.md da change backend-mcp-catalogo-vinculo) — id + name, não o
/// registro completo.
/// </summary>
public sealed record McpServerSummaryResponse(Guid Id, string Name)
{
    public static McpServerSummaryResponse FromEntity(McpServer mcpServer) => new(mcpServer.Id, mcpServer.Name);
}
