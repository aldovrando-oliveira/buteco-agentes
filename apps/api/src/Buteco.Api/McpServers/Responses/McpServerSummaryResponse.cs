using Buteco.Api.McpServers.Entities;

namespace Buteco.Api.McpServers.Responses;

/// <summary>
/// Nível de detalhe usado em <c>AgentResponse.McpServers</c> (Decision 5 do
/// design.md da change backend-mcp-catalogo-vinculo) — id + name, não o
/// registro completo do <c>McpServer</c>. <see cref="AllowedTools"/> reflete
/// as tools permitidas nesse vínculo específico (change
/// backend-mcp-selecao-tools) — não confundir com o catálogo de tools
/// oferecido pelo servidor (<c>GET /mcp-servers/{id}/tools</c>).
/// </summary>
public sealed record McpServerSummaryResponse(Guid Id, string Name, IReadOnlyList<string> AllowedTools)
{
    public static McpServerSummaryResponse FromEntity(McpServer mcpServer, IReadOnlyList<string> allowedTools) => new(mcpServer.Id, mcpServer.Name, allowedTools);
}
