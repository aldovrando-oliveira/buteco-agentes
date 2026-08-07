using Buteco.Api.Agents.Responses;

namespace Buteco.Api.AgentDelegations.Commands.ReplaceAgentDelegations;

/// <summary>
/// Sinal neutro devolvido pelo handler — sem nenhuma referência a
/// <c>Microsoft.AspNetCore.Http.HttpResults</c>, mesmo padrão de
/// <c>ReplaceAgentMcpServersResult</c>. <see cref="InvalidTargetAgentIds"/>
/// distingue "agente Source inexistente" (404) de "algum TargetAgentId não
/// existe" (400); <see cref="SelfDelegationRejected"/> distingue
/// auto-delegação (400, Decision 2 do design.md) das demais rejeições.
/// Rejeição sempre atômica: nenhum caso de erro aplica nenhum vínculo do
/// payload.
/// </summary>
public sealed record ReplaceAgentDelegationsResult(
    AgentResponse? Agent,
    bool AgentFound,
    IReadOnlyList<Guid> InvalidTargetAgentIds,
    bool SelfDelegationRejected)
{
    public static ReplaceAgentDelegationsResult AgentNotFound() => new(null, AgentFound: false, [], SelfDelegationRejected: false);

    public static ReplaceAgentDelegationsResult SelfDelegation() => new(null, AgentFound: true, [], SelfDelegationRejected: true);

    public static ReplaceAgentDelegationsResult InvalidIds(IReadOnlyList<Guid> invalidIds) => new(null, AgentFound: true, invalidIds, SelfDelegationRejected: false);

    public static ReplaceAgentDelegationsResult Success(AgentResponse agent) => new(agent, AgentFound: true, [], SelfDelegationRejected: false);
}
