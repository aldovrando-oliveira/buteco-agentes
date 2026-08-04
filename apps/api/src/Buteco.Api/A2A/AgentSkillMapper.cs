using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using global::A2A;
using Buteco.Api.Agents.Entities;

namespace Buteco.Api.A2A;

// Mapeia Skill (shape provisório do domínio, registrado em
// backend-agente-description-skills) para o AgentSkill exigido pelo
// protocolo A2A (Decision 4 do design.md de backend-a2a-agent-card).
public static class AgentSkillMapper
{
    public static IReadOnlyList<AgentSkill> MapSkills(IReadOnlyList<Skill> skills)
    {
        var usedIds = new HashSet<string>();
        var result = new List<AgentSkill>(skills.Count);

        foreach (var skill in skills)
        {
            var baseId = Slugify(skill.Name);
            var id = baseId;
            var suffix = 2;
            while (!usedIds.Add(id))
            {
                id = $"{baseId}-{suffix}";
                suffix++;
            }

            result.Add(new AgentSkill
            {
                Id = id,
                Name = skill.Name,
                Description = skill.Description ?? string.Empty,
                Tags = [],
            });
        }

        return result;
    }

    // Slug determinístico: mesmo Name sempre produz o mesmo Id, estável
    // entre requisições (ao contrário de um índice posicional, que muda se
    // a ordem das Skills mudar num PUT sem nenhuma mudança de conteúdo).
    public static string Slugify(string name)
    {
        var normalized = name.Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                withoutDiacritics.Append(c);
            }
        }

        var slug = Regex.Replace(withoutDiacritics.ToString().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 0 ? slug : "skill";
    }
}
