namespace Buteco.Api.AgentMcpBindings.Entities;

/// <summary>
/// Vínculo N:N entre <see cref="Buteco.Api.Agents.Entities.Agent"/> e
/// <see cref="Buteco.Api.McpServers.Entities.McpServer"/>, sem dado próprio
/// além das duas chaves estrangeiras (sem filtro de tool individual nesta
/// fatia — ver Non-Goals do design.md).
/// </summary>
public class AgentMcpServer
{
    public Guid AgentId { get; private set; }

    public Guid McpServerId { get; private set; }

    private AgentMcpServer()
    {
    }

    public AgentMcpServer(Guid agentId, Guid mcpServerId)
    {
        AgentId = agentId;
        McpServerId = mcpServerId;
    }
}
