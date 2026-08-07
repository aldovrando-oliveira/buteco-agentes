using Buteco.Api.Agents.Entities;
using Buteco.Api.McpServers.Responses;

namespace Buteco.Api.Agents.Responses;

public record AgentResponse(
    Guid Id,
    string Name,
    string Instructions,
    bool IsActive,
    string? Provider,
    string? Model,
    string? Description,
    IReadOnlyList<SkillResponse> Skills,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<McpServerSummaryResponse> McpServers,
    IReadOnlyList<AgentSummaryResponse> DelegatesTo)
{
    public static AgentResponse FromEntity(Agent agent, IReadOnlyList<McpServerSummaryResponse> mcpServers, IReadOnlyList<AgentSummaryResponse> delegatesTo) =>
        new(
            agent.Id,
            agent.Name,
            agent.Instructions,
            agent.IsActive,
            agent.Provider,
            agent.Model,
            agent.Description,
            agent.Skills.Select(SkillResponse.FromEntity).ToList(),
            agent.CreatedAt,
            agent.UpdatedAt,
            mcpServers,
            delegatesTo);
}
