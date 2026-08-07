namespace Buteco.Api.AgentDelegations.Entities;

/// <summary>
/// Vínculo unidirecional de delegação entre dois <see cref="Buteco.Api.Agents.Entities.Agent"/>
/// (Source → Target). Tabela relacional própria, não jsonb, porque os dois
/// lados são entidades catalogadas com identidade própria (Decision 1 do
/// design.md).
/// </summary>
public class AgentDelegation
{
    public Guid SourceAgentId { get; private set; }

    public Guid TargetAgentId { get; private set; }

    private AgentDelegation()
    {
    }

    public AgentDelegation(Guid sourceAgentId, Guid targetAgentId)
    {
        SourceAgentId = sourceAgentId;
        TargetAgentId = targetAgentId;
    }
}
