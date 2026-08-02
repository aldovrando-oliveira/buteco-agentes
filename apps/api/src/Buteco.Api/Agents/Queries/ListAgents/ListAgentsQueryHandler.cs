using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Agents.Queries.ListAgents;

public sealed class ListAgentsQueryHandler(AppDbContext dbContext) : IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentResponse>>
{
    public async ValueTask<IReadOnlyList<AgentResponse>> Handle(ListAgentsQuery query, CancellationToken cancellationToken)
    {
        var agents = await dbContext.Agents
            .AsNoTracking()
            .OrderBy(agent => agent.CreatedAt)
            .ToListAsync(cancellationToken);

        // Uma única consulta para todos os vínculos, em vez de N+1 por
        // agente (mesmo espírito de AgentMcpServerLookup, mas em lote).
        var bindings = await dbContext.AgentMcpServers
            .AsNoTracking()
            .Join(dbContext.McpServers, binding => binding.McpServerId, mcpServer => mcpServer.Id, (binding, mcpServer) => new { binding.AgentId, McpServer = mcpServer })
            .ToListAsync(cancellationToken);

        var mcpServersByAgentId = bindings
            .GroupBy(binding => binding.AgentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<McpServerSummaryResponse>)group
                    .OrderBy(binding => binding.McpServer.Name)
                    .Select(binding => McpServerSummaryResponse.FromEntity(binding.McpServer))
                    .ToList());

        return agents
            .Select(agent => AgentResponse.FromEntity(agent, mcpServersByAgentId.GetValueOrDefault(agent.Id, [])))
            .ToList();
    }
}
