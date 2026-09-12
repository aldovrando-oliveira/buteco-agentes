using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Buteco.Workers.Naming;

/// <summary>
/// Converte um nome livre de cadastro (<c>Agent.Name</c>, <c>KnowledgeBase.Name</c>)
/// no pedaço legível que entra no nome de tool exposto ao LLM.
///
/// <para>
/// Mirror de <c>Buteco.Api.A2A.AgentSkillMapper.Slugify</c> (change
/// backend-a2a-agent-card) — os nomes de cadastro não têm unicidade garantida,
/// mesma lacuna já registrada ali para <c>Skill.Name</c> (ver design.md da
/// change apps-workers-delegacao-execucao, Decision 9). Dedupe determinístico
/// por sufixo numérico **não** mora aqui: é global, em
/// <see cref="Buteco.Workers.Mcp.ToolNameDeduplicator"/>, no ponto que une os
/// três conjuntos de tools (change dedupe-global-nome-de-tool, Decisão 1).
/// </para>
///
/// <para>
/// <b>Dois consumidores, e é por isso que o tipo saiu de <c>AgentDelegations/</c>
/// para cá</b> (change knowledge-tool-resolver, D3): a tool de delegação
/// (<c>delegate_to_&lt;slug&gt;</c>) e a tool de conhecimento
/// (<c>search_&lt;slug&gt;</c>). A convenção 2 pede 2-3 consumidores reais antes
/// de extrair, e este é o segundo. Movimento puro, sem mudança de comportamento.
/// </para>
///
/// <para>
/// <b>Não confundir com <see cref="Buteco.Workers.Mcp.ToolNameSanitizer"/>:</b>
/// os dois rodam em sequência e fazem coisas diferentes. Este **remove
/// diacríticos** via <see cref="NormalizationForm.FormD"/> e produz
/// <c>kebab-case</c>; o sanitizador troca o que sobrar fora de
/// <c>[a-zA-Z0-9_-]</c> por <c>_</c> e trunca em 64. Pular este e usar só o
/// sanitizador é o que produz o padrão <c>Informa__es_Gerais</c> encontrado no
/// censo de nomes — <c>ç</c> e <c>õ</c> viram um <c>_</c> cada, e o <c>__</c>
/// resultante deixa de distinguir o separador.
/// </para>
/// </summary>
public static class ToolNameSlugifier
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
