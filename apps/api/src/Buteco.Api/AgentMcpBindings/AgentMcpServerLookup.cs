using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Responses;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.AgentMcpBindings;

/// <summary>
/// Consulta compartilhada dos servidores MCP vinculados a um agente, usada
/// por todos os handlers de <c>Agents</c> que retornam <c>AgentResponse</c>
/// (Decision 5 do design.md da change backend-mcp-catalogo-vinculo — id +
/// name, não o registro completo).
/// </summary>
public static class AgentMcpServerLookup
{
    public static async Task<IReadOnlyList<McpServerSummaryResponse>> GetLinkedMcpServersAsync(
        AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken)
    {
        return await dbContext.AgentMcpServers
            .AsNoTracking()
            .Where(binding => binding.AgentId == agentId)
            .Join(dbContext.McpServers, binding => binding.McpServerId, mcpServer => mcpServer.Id, (_, mcpServer) => mcpServer)
            .OrderBy(mcpServer => mcpServer.Name)
            .Select(mcpServer => McpServerSummaryResponse.FromEntity(mcpServer))
            .ToListAsync(cancellationToken);
    }
}
