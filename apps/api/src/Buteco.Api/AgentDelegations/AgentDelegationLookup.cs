using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.AgentDelegations;

/// <summary>
/// Consulta compartilhada dos agentes para os quais um agente delega
/// (delegações de saída), usada por todos os handlers de <c>Agents</c> que
/// retornam <c>AgentResponse</c> (Decision 6 do design.md da change
/// backend-agente-delegacao-catalogo-vinculo — id + name, não o registro
/// completo).
/// </summary>
public static class AgentDelegationLookup
{
    public static async Task<IReadOnlyList<AgentSummaryResponse>> GetDelegateTargetsAsync(
        AppDbContext dbContext, Guid sourceAgentId, CancellationToken cancellationToken)
    {
        return await dbContext.AgentDelegations
            .AsNoTracking()
            .Where(delegation => delegation.SourceAgentId == sourceAgentId)
            .Join(dbContext.Agents, delegation => delegation.TargetAgentId, agent => agent.Id, (delegation, agent) => agent)
            // O ThenBy NÃO é redundante: nome de agente não é único — o
            // AppDbContext não tem índice único de nome e nenhum handler de
            // criação valida duplicata —, então OrderBy(Name) sozinho deixa a
            // ordem entre homônimos a cargo do plano do Postgres, e a mesma
            // requisição pode responder em ordens diferentes sem nada ter
            // mudado no cadastro (api-response-ordering).
            .OrderBy(agent => agent.Name)
            .ThenBy(agent => agent.Id)
            .Select(agent => AgentSummaryResponse.FromEntity(agent))
            .ToListAsync(cancellationToken);
    }
}
