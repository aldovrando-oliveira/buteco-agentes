using Buteco.Api.Agents.Entities;

namespace Buteco.Api.Agents.Responses;

public record AgentResponse(
    Guid Id,
    string Name,
    string Instructions,
    bool IsActive,
    string? Provider,
    string? Model,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static AgentResponse FromEntity(Agent agent) =>
        new(agent.Id, agent.Name, agent.Instructions, agent.IsActive, agent.Provider, agent.Model, agent.CreatedAt, agent.UpdatedAt);
}
