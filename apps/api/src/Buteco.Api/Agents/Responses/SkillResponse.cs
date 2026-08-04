using Buteco.Api.Agents.Entities;

namespace Buteco.Api.Agents.Responses;

public sealed record SkillResponse(string Name, string? Description)
{
    public static SkillResponse FromEntity(Skill skill) => new(skill.Name, skill.Description);
}
