using Buteco.Api.Agents.Responses;

namespace Buteco.Api.AgentDelegations.Commands.ReplaceAgentDelegations;

/// <summary>
/// Sinal neutro devolvido pelo handler — sem nenhuma referência a
/// <c>Microsoft.AspNetCore.Http.HttpResults</c>, mesmo padrão de
/// <c>ReplaceAgentMcpServersResult</c>. <see cref="InvalidTargetAgentIds"/>
/// distingue "agente Source inexistente" (404) de "algum TargetAgentId não
/// existe" (400); <see cref="SelfDelegationRejected"/> distingue
/// auto-delegação (400, Decision 2 do design.md) das demais rejeições;
/// <see cref="CycleAgentNames"/> distingue a recusa por ciclo e já carrega o
/// caminho resolvido em nomes, porque é o handler que tem o
/// <c>AppDbContext</c> para resolvê-los.
/// Rejeição sempre atômica: nenhum caso de erro aplica nenhum vínculo do
/// payload.
/// </summary>
public sealed record ReplaceAgentDelegationsResult(
    AgentResponse? Agent,
    bool AgentFound,
    IReadOnlyList<Guid> InvalidTargetAgentIds,
    bool SelfDelegationRejected,
    IReadOnlyList<string> CycleAgentNames)
{
    public static ReplaceAgentDelegationsResult AgentNotFound() => new(null, AgentFound: false, [], SelfDelegationRejected: false, []);

    public static ReplaceAgentDelegationsResult SelfDelegation() => new(null, AgentFound: true, [], SelfDelegationRejected: true, []);

    public static ReplaceAgentDelegationsResult InvalidIds(IReadOnlyList<Guid> invalidIds) => new(null, AgentFound: true, invalidIds, SelfDelegationRejected: false, []);

    /// <param name="cycleAgentNames">
    /// O caminho que fecha o ciclo, em ordem, começando e terminando no agente
    /// Source. Nomes e não ids: a mensagem precisa dizer QUAL vínculo desfazer, e
    /// o vínculo a desfazer pode estar em outro agente — quem lê a tela lê nome
    /// (design.md, D5).
    /// </param>
    public static ReplaceAgentDelegationsResult Cycle(IReadOnlyList<string> cycleAgentNames) =>
        new(null, AgentFound: true, [], SelfDelegationRejected: false, cycleAgentNames);

    public static ReplaceAgentDelegationsResult Success(AgentResponse agent) => new(agent, AgentFound: true, [], SelfDelegationRejected: false, []);
}
