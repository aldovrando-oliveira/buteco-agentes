using Buteco.Api.A2A;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Agents.Queries.ListAgents;

public sealed class ListAgentsQueryHandler(AppDbContext dbContext,
    IOptions<PublicUrlOptions> publicUrlOptions) : IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentResponse>>
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
            .Join(dbContext.McpServers, binding => binding.McpServerId, mcpServer => mcpServer.Id, (binding, mcpServer) => new { binding.AgentId, binding.AllowedTools, McpServer = mcpServer })
            .ToListAsync(cancellationToken);

        var mcpServersByAgentId = bindings
            .GroupBy(binding => binding.AgentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<McpServerSummaryResponse>)group
                    .OrderBy(binding => binding.McpServer.Name)
                    .Select(binding => McpServerSummaryResponse.FromEntity(binding.McpServer, binding.AllowedTools))
                    .ToList());

        // Mesmo espírito do lote acima, agora para delegações de saída
        // (AgentDelegationLookup faz o equivalente por agente, mas em lote
        // aqui evita N+1 para a listagem inteira).
        var delegations = await dbContext.AgentDelegations
            .AsNoTracking()
            .Join(dbContext.Agents, delegation => delegation.TargetAgentId, agent => agent.Id, (delegation, agent) => new { delegation.SourceAgentId, TargetAgent = agent })
            .ToListAsync(cancellationToken);

        var delegatesToBySourceAgentId = delegations
            .GroupBy(delegation => delegation.SourceAgentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AgentSummaryResponse>)group
                    .OrderBy(delegation => delegation.TargetAgent.Name)
                    .Select(delegation => AgentSummaryResponse.FromEntity(delegation.TargetAgent))
                    .ToList());

        return agents
            .Select(agent => AgentResponse.FromEntity(
                agent,
                mcpServersByAgentId.GetValueOrDefault(agent.Id, []),
                delegatesToBySourceAgentId.GetValueOrDefault(agent.Id, []),
                AgentA2AAddressBuilder.Build(publicUrlOptions.Value, agent.Id)))
            .ToList();
    }
}
