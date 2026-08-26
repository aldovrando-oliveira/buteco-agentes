using Buteco.Workers.Agents.Entities;
using Buteco.Workers.Infrastructure;
using Microsoft.Extensions.AI;

namespace Buteco.Workers.AgentDelegations;

/// <summary>
/// Resolve o conjunto de tools de delegação disponível para um agente numa
/// execução: para cada <c>AgentDelegation</c> vinculado (Source → Target),
/// monta uma <see cref="AITool"/> que, quando chamada, cria e aguarda uma
/// task real para o Target — mesmo papel de
/// <see cref="Buteco.Workers.Mcp.IMcpToolSetResolver"/> para tools MCP (ver
/// design.md da change apps-workers-delegacao-execucao, Decision 10).
/// </summary>
public interface IAgentDelegationToolSetResolver
{
    /// <param name="dbContext">
    /// Reaproveitado do escopo já aberto por quem chama (mesmo
    /// <see cref="AppDbContext"/> usado para ler o <c>Agent</c> da execução) —
    /// mesmo padrão de <see cref="Buteco.Workers.Mcp.IMcpToolSetResolver"/>.
    /// </param>
    /// <param name="sourceAgent">
    /// Agent do Source já carregado no início de <c>AgentExecutionService.ExecuteAsync</c> —
    /// usado para montar a lista de delegações vinculadas. A checagem de
    /// <c>IsActive</c>/<c>Provider</c>/<c>Model</c> do Source no momento da
    /// chamada da tool relê o agente fresco do banco (mesmo padrão do
    /// Target), não reaproveita este snapshot — ver design.md, Decision 5.
    /// </param>
    /// <param name="contextId">
    /// Propagado para a task delegada, mesmo <c>contextId</c> da conversa em
    /// andamento (Decision 7).
    /// </param>
    /// <param name="currentDepth">
    /// Profundidade da própria task do Source nesta cadeia de delegação —
    /// a task criada para o Target recebe <c>currentDepth + 1</c> (Decision 6).
    /// </param>
    /// <param name="messageInstant">
    /// Instante em que a mensagem original do usuário foi enviada, já
    /// extraído da própria task do Source (ver
    /// <c>Buteco.Workers.Agents.AgentExecutionService</c>) — propagado para
    /// a task criada para o Target, para que Source e Target resolvam
    /// expressões de tempo relativas contra o mesmo instante mesmo quando
    /// processados em momentos de relógio diferentes (design.md da change
    /// inbox-instante-mensagem, Decisão D3). <c>null</c> quando o Source não
    /// tinha um instante de mensagem disponível — a task do Target não
    /// inventa um valor.
    /// </param>
    Task<IReadOnlyList<AITool>> ResolveAsync(
        AppDbContext dbContext,
        Agent sourceAgent,
        string contextId,
        int currentDepth,
        DateTimeOffset? messageInstant,
        CancellationToken cancellationToken);
}
