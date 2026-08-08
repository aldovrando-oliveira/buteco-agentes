using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Buteco.Workers.AgentDelegations;

/// <summary>
/// Mirror de <c>Buteco.Api.A2A.AgentSkillMapper.Slugify</c> (change
/// backend-a2a-agent-card) — <c>Agent.Name</c> não tem unicidade garantida,
/// mesma lacuna já registrada ali para <c>Skill.Name</c> (ver design.md da
/// change apps-workers-delegacao-execucao, Decision 9). Dedupe determinístico
/// por sufixo numérico fica com quem monta o conjunto de tools
/// (<see cref="AgentDelegationToolSetResolver"/>), mesmo padrão de
/// <c>AgentSkillMapper.MapSkills</c>.
/// </summary>
public static class DelegationToolNameSlugifier
{
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
        return slug.Length > 0 ? slug : "agent";
    }
}
