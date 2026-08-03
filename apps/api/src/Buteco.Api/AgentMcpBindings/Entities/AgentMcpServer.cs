namespace Buteco.Api.AgentMcpBindings.Entities;

/// <summary>
/// Vínculo N:N entre <see cref="Buteco.Api.Agents.Entities.Agent"/> e
/// <see cref="Buteco.Api.McpServers.Entities.McpServer"/>. <see cref="AllowedTools"/>
/// restringe quais tools daquele servidor o agente pode usar (change
/// backend-mcp-selecao-tools) — armazenado como jsonb, não tabela filha,
/// porque tools não são um catálogo relacional persistido em lugar nenhum
/// (são descobertas ao vivo via `tools/list`; ver Decision 1 do design.md).
/// </summary>
public class AgentMcpServer
{
    public Guid AgentId { get; private set; }

    public Guid McpServerId { get; private set; }

    public IReadOnlyList<string> AllowedTools { get; private set; } = [];

    private AgentMcpServer()
    {
    }

    public AgentMcpServer(Guid agentId, Guid mcpServerId, IReadOnlyList<string> allowedTools)
    {
        AgentId = agentId;
        McpServerId = mcpServerId;
        AllowedTools = allowedTools;
    }
}
