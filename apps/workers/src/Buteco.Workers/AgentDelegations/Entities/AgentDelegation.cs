namespace Buteco.Workers.AgentDelegations.Entities;

/// <summary>
/// Projeção somente-leitura da entidade <c>AgentDelegation</c> de <c>apps/api</c>,
/// contra o mesmo schema Postgres — sem <c>ProjectReference</c> entre os dois
/// apps (mesmo padrão de <see cref="Buteco.Workers.Mcp.Entities.McpServer"/>/
/// <see cref="Buteco.Workers.Mcp.Entities.AgentMcpServer"/>).
/// </summary>
public class AgentDelegation
{
    public Guid SourceAgentId { get; private set; }

    public Guid TargetAgentId { get; private set; }

    private AgentDelegation()
    {
    }
}
