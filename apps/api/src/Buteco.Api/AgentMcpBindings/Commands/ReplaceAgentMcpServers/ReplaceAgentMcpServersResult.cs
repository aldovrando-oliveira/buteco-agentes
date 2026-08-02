using Buteco.Api.Agents.Responses;

namespace Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;

/// <summary>
/// Sinal neutro devolvido pelo handler — sem nenhuma referência a
/// <c>Microsoft.AspNetCore.Http.HttpResults</c>, mesmo padrão de
/// <c>UpdateAgentResult</c>. <see cref="InvalidMcpServerIds"/> distingue
/// "agente inexistente" (404) de "algum McpServerId não existe" (400).
/// </summary>
public sealed record ReplaceAgentMcpServersResult(AgentResponse? Agent, bool AgentFound, IReadOnlyList<Guid> InvalidMcpServerIds)
{
    public static ReplaceAgentMcpServersResult AgentNotFound() => new(null, AgentFound: false, []);

    public static ReplaceAgentMcpServersResult InvalidIds(IReadOnlyList<Guid> invalidIds) => new(null, AgentFound: true, invalidIds);

    public static ReplaceAgentMcpServersResult Success(AgentResponse agent) => new(agent, AgentFound: true, []);
}
