using Buteco.Api.Agents.Responses;

namespace Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;

/// <summary>
/// Sinal neutro devolvido pelo handler — sem nenhuma referência a
/// <c>Microsoft.AspNetCore.Http.HttpResults</c>, mesmo padrão de
/// <c>UpdateAgentResult</c>. <see cref="InvalidMcpServerIds"/> distingue
/// "agente inexistente" (404) de "algum McpServerId não existe" (400);
/// <see cref="InvalidTools"/> distingue tool inexistente no servidor (400)
/// de <see cref="HandshakeFailure"/>, falha de conexão durante a validação
/// (502 — Decision 6 do design.md da change backend-mcp-selecao-tools).
/// Rejeição sempre atômica: nenhum caso de erro aplica nenhum vínculo do
/// payload.
/// </summary>
public sealed record ReplaceAgentMcpServersResult(
    AgentResponse? Agent,
    bool AgentFound,
    IReadOnlyList<Guid> InvalidMcpServerIds,
    IReadOnlyList<InvalidMcpServerTool> InvalidTools,
    McpServerHandshakeFailure? HandshakeFailure)
{
    public static ReplaceAgentMcpServersResult AgentNotFound() => new(null, AgentFound: false, [], [], null);

    public static ReplaceAgentMcpServersResult InvalidIds(IReadOnlyList<Guid> invalidIds) => new(null, AgentFound: true, invalidIds, [], null);

    public static ReplaceAgentMcpServersResult InvalidToolsFound(IReadOnlyList<InvalidMcpServerTool> invalidTools) => new(null, AgentFound: true, [], invalidTools, null);

    public static ReplaceAgentMcpServersResult HandshakeFailed(McpServerHandshakeFailure failure) => new(null, AgentFound: true, [], [], failure);

    public static ReplaceAgentMcpServersResult Success(AgentResponse agent) => new(agent, AgentFound: true, [], [], null);
}
