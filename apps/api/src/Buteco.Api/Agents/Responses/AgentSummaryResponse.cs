using Buteco.Api.Agents.Entities;

namespace Buteco.Api.Agents.Responses;

/// <summary>
/// Nível de detalhe usado em <c>AgentResponse.DelegatesTo</c> (Decision 6
/// do design.md da change backend-agente-delegacao-catalogo-vinculo) — id +
/// name, não o registro completo do <c>Agent</c>. Nomeado pela entidade
/// resumida (<c>Agent</c>), não pelo vínculo (<c>AgentDelegation</c>),
/// porque não há nenhum dado por vínculo a expor — reutilizável se um
/// campo simétrico futuro precisar do mesmo shape.
/// </summary>
public sealed record AgentSummaryResponse(Guid Id, string Name)
{
    public static AgentSummaryResponse FromEntity(Agent agent) => new(agent.Id, agent.Name);
}
