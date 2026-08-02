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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<McpServerSummaryResponse> McpServers)
{
    public static AgentResponse FromEntity(Agent agent, IReadOnlyList<McpServerSummaryResponse> mcpServers) =>
        new(agent.Id, agent.Name, agent.Instructions, agent.IsActive, agent.Provider, agent.Model, agent.CreatedAt, agent.UpdatedAt, mcpServers);
}
