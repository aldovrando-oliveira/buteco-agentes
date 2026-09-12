using Buteco.Workers.Infrastructure;
using Microsoft.Extensions.AI;

namespace Buteco.Workers.Knowledge.Execution;

/// <summary>
/// Resolve o conjunto de tools de conhecimento disponível para um agente numa
/// execução: para cada <c>AgentKnowledgeBase</c> vinculada cuja
/// <c>KnowledgeBase.IsActive</c> é verdadeira, monta uma <see cref="AITool"/>
/// que busca por proximidade vetorial **naquela base**.
///
/// <para>
/// Mesmo papel de <see cref="Buteco.Workers.Mcp.IMcpToolSetResolver"/> e de
/// <see cref="Buteco.Workers.AgentDelegations.IAgentDelegationToolSetResolver"/>
/// — o terceiro conjunto do namespace de tool que
/// <see cref="Buteco.Workers.Mcp.ToolNameDeduplicator"/> administra.
/// </para>
/// </summary>
public interface IKnowledgeToolSetResolver
{
    /// <param name="dbContext">
    /// Reaproveitado do escopo já aberto por quem chama (o mesmo
    /// <see cref="AppDbContext"/> usado para ler o <c>Agent</c> da execução) —
    /// o resolver não abre escopo nem conexão própria, mesmo padrão dos outros
    /// dois resolvedores.
    /// </param>
    /// <param name="agentId">
    /// Só o id, e é o ponto em que esta assinatura é a de
    /// <see cref="Buteco.Workers.Mcp.IMcpToolSetResolver"/> e **não** a de
    /// delegação: não são necessários <c>sourceAgent</c>, <c>contextId</c>,
    /// <c>currentDepth</c> nem <c>messageInstant</c>. A busca não cria task, não
    /// entra em cadeia de delegação e não resolve tempo relativo — ela lê o
    /// índice e devolve trecho (design.md, V1).
    /// </param>
    /// <returns>
    /// Lista simples, **não descartável**. Diferente de
    /// <c>McpToolSet</c>, não há conexão externa viva por trás de cada tool: a
    /// busca usa o <paramref name="dbContext"/> de quem chamou e o gerador de
    /// embedding é resolvido dentro da invocação, não aqui (design.md, D7/D9).
    /// </returns>
    Task<IReadOnlyList<AITool>> ResolveAsync(AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken);
}
