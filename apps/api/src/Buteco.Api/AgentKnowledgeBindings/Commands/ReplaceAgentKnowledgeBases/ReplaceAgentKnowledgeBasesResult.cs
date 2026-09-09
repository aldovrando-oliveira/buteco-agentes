using Buteco.Api.Agents.Responses;

namespace Buteco.Api.AgentKnowledgeBindings.Commands.ReplaceAgentKnowledgeBases;

/// <summary>
/// Sinal neutro devolvido pelo handler — sem nenhuma referência a
/// <c>Microsoft.AspNetCore.Http.HttpResults</c>, mesmo padrão de
/// <c>ReplaceAgentDelegationsResult</c>. <see cref="InvalidKnowledgeBaseIds"/>
/// distingue "agente inexistente" (404) de "algum KnowledgeBaseId não existe"
/// (400). Rejeição sempre atômica: nenhum caso de erro aplica nenhum vínculo do
/// payload nem remove os anteriores.
/// </summary>
/// <remarks>
/// <b>Não há contraparte de <c>SelfDelegationRejected</c>, e a ausência é
/// deliberada</b> (design.md, V4): <c>AgentDelegation</c> precisa rejeitar
/// auto-delegação porque os dois lados são <c>Agent</c>. Aqui os lados são
/// entidades diferentes (<c>Agent</c> × <c>KnowledgeBase</c>), então não existe
/// caso análogo a rejeitar. Dito em vez de omitido porque "não se aplica" e
/// "esqueceram" se parecem no código.
/// </remarks>
public sealed record ReplaceAgentKnowledgeBasesResult(
    AgentResponse? Agent,
    bool AgentFound,
    IReadOnlyList<Guid> InvalidKnowledgeBaseIds)
{
    public static ReplaceAgentKnowledgeBasesResult AgentNotFound() => new(null, AgentFound: false, []);

    public static ReplaceAgentKnowledgeBasesResult InvalidIds(IReadOnlyList<Guid> invalidIds) => new(null, AgentFound: true, invalidIds);

    public static ReplaceAgentKnowledgeBasesResult Success(AgentResponse agent) => new(agent, AgentFound: true, []);
}
