namespace Buteco.Workers.Mcp.Entities;

/// <summary>
/// Projeção independente da entidade <c>AgentMcpServer</c> de <c>apps/api</c>
/// (vínculo N:N agente↔MCP, com <see cref="AllowedTools"/> — coluna jsonb,
/// ver design.md da change <c>backend-mcp-selecao-tools</c>), contra o mesmo
/// schema Postgres — sem <c>ProjectReference</c> entre os dois apps.
/// Read-only: <c>apps/workers</c> nunca escreve nesta tabela.
/// </summary>
public class AgentMcpServer
{
    public Guid AgentId { get; private set; }

    public Guid McpServerId { get; private set; }

    public IReadOnlyList<string> AllowedTools { get; private set; } = [];

    private AgentMcpServer()
    {
    }
}
