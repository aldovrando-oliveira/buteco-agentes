using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Buteco.Workers.Mcp;

/// <summary>
/// Garante nomes únicos no conjunto final de tools entregue ao LLM de um agente
/// — a união das tools MCP, das tools de delegação e das tools de conhecimento.
/// </summary>
/// <remarks>
/// <para>
/// Mora no ponto de concatenação, e não dentro de cada resolvedor, porque é o
/// único ponto do sistema que sabe que os três conjuntos dividem namespace
/// (design.md da change dedupe-global-nome-de-tool, Decisão 1; o terceiro
/// conjunto entrou em knowledge-tool-resolver, D8). Cada resolvedor,
/// por construção, não sabe — e é essa ignorância que produzia o defeito: sem
/// dedupe, <c>FunctionInvokingChatClient.FindTool</c> resolve pelo primeiro
/// match ordinal e sombreia o resto <b>em silêncio</b>, enquanto as duas
/// declarações vão no payload para o provedor com o mesmo nome.
/// </para>
/// <para>
/// Colisão é resolvida <b>renomeando</b>, nunca descartando: as duas tools
/// permanecem no conjunto e continuam roteando para o seu destino real, porque o
/// nome exposto é rótulo e não chave de resolução de volta (V1/V6 do design.md).
/// </para>
/// </remarks>
public sealed class ToolNameDeduplicator(ILogger<ToolNameDeduplicator> logger)
{
    /// <summary>
    /// Une os três conjuntos resolvendo colisões de nome. <b>A ordem dos
    /// parâmetros é a precedência declarada</b> (Decisão 5, estendida em
    /// knowledge-tool-resolver D8): tool MCP mantém o nome pretendido, tool de
    /// delegação é renomeada contra MCP, e tool de conhecimento é renomeada
    /// contra as duas. Não é consequência da ordem de operandos de
    /// <c>Concat</c>, como era antes da change de dedupe.
    /// </summary>
    /// <remarks>
    /// Conhecimento entra por último, e o critério é o mesmo que pôs delegação
    /// depois de MCP: quem chega depois é o renomeado, e conhecimento é o
    /// conjunto mais novo e o que tem menos nome em uso — renomear uma tool de
    /// conhecimento é a mudança que quebra menos.
    /// </remarks>
    public IReadOnlyList<AITool> Deduplicate(
        Guid agentId,
        IReadOnlyList<AITool> mcpTools,
        IReadOnlyList<AITool> delegationTools,
        IReadOnlyList<AITool> knowledgeTools)
    {
        // StringComparer.Ordinal explícito, nunca o default implícito: a fonte
        // da escolha é FunctionInvokingChatClient.FindTool, que casa com
        // string.Equals(..., StringComparison.Ordinal) — sensível a caixa. Um
        // comparador insensível aqui renomearia pares que não colidem no
        // runtime que de fato resolve a chamada, mudando o nome de tool de
        // agente que estava correto (Decisão 9).
        var owners = new Dictionary<string, ToolOrigin>(StringComparer.Ordinal);
        var deduplicated = new List<AITool>(mcpTools.Count + delegationTools.Count + knowledgeTools.Count);

        AppendAll(deduplicated, owners, agentId, mcpTools, ToolOrigin.Mcp);
        AppendAll(deduplicated, owners, agentId, delegationTools, ToolOrigin.Delegation);
        AppendAll(deduplicated, owners, agentId, knowledgeTools, ToolOrigin.Knowledge);

        return deduplicated;
    }

    private void AppendAll(
        List<AITool> deduplicated,
        Dictionary<string, ToolOrigin> owners,
        Guid agentId,
        IReadOnlyList<AITool> tools,
        ToolOrigin origin)
    {
        foreach (var tool in tools)
        {
            var intendedName = tool.Name;
            if (owners.TryAdd(intendedName, origin))
            {
                deduplicated.Add(tool);
                continue;
            }

            var finalName = NextFreeName(owners, intendedName, origin);

            logger.LogWarning(
                "Colisão de nome de tool no agente {AgentId}: '{IntendedName}' já usado por uma tool de {WinnerOrigin}; a tool de {LoserOrigin} foi exposta como '{FinalName}'.",
                agentId,
                intendedName,
                owners[intendedName],
                origin,
                finalName);

            deduplicated.Add(Rename(tool, finalName));
        }
    }

    /// <summary>
    /// Sufixo numérico a partir de 2, primeiro livre vence — mesmo idioma que
    /// <c>AgentDelegationToolSetResolver</c> usava localmente antes desta
    /// change, agora global (Decisão 3). O sufixo cabe <b>dentro</b> do limite:
    /// a base encurta para abrir espaço, em vez de concatenar às cegas e
    /// estourar (Decisão 4 — o dedupe local produzia 66 caracteres quando a base
    /// já estava nos 64).
    /// </summary>
    private static string NextFreeName(Dictionary<string, ToolOrigin> owners, string baseName, ToolOrigin origin)
    {
        var suffix = 2;
        while (true)
        {
            // O encurtamento é reaplicado a cada tentativa, não uma vez só: o
            // sufixo muda de tamanho ao passar de 9 para 10, e a base precisa
            // ceder o caractere correspondente.
            var candidate = ToolNameSanitizer.AppendSuffixWithinLimit(baseName, $"-{suffix}");
            if (owners.TryAdd(candidate, origin))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static AITool Rename(AITool tool, string finalName) => tool switch
    {
        // Preserva o tipo concreto e o ProtocolTool — a chamada remota segue
        // usando o nome original da tool no servidor MCP, e é o mecanismo que o
        // próprio SDK do MCP documenta para colisão entre fontes (V1).
        McpClientTool mcpTool => mcpTool.WithName(finalName),

        // Encapsula sem precisar do delegate capturado no closure (V6).
        AIFunction function => new RenamedAIFunction(function, finalName),

        // Nenhuma tool destes três conjuntos cai aqui hoje (os três lados
        // produzem AIFunction), mas AITool não expõe nome gravável, então uma
        // origem futura que não seja AIFunction não é renomeável — fica como
        // está, e o aviso já emitido registra a colisão não resolvida.
        _ => tool,
    };
}
