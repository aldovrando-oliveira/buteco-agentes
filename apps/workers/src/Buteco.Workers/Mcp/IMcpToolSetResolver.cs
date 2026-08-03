using Buteco.Workers.Infrastructure;

namespace Buteco.Workers.Mcp;

/// <summary>
/// Resolve o conjunto de tools MCP disponível para um agente numa execução:
/// para cada <c>AgentMcpServer</c> vinculado com <c>McpServer.IsActive</c>,
/// conecta, descobre as tools (`tools/list`), filtra pela interseção com
/// <c>AllowedTools</c>, e devolve um <see cref="McpToolSet"/> — mesmo padrão
/// de <see cref="Agents.IChatClientResolver"/> (interface só para permitir
/// substituição em teste).
/// </summary>
public interface IMcpToolSetResolver
{
    /// <param name="dbContext">
    /// Reaproveitado do escopo já aberto por quem chama (mesmo
    /// <see cref="AppDbContext"/> usado para ler o <c>Agent</c> da execução) —
    /// o resolver não abre um escopo/conexão de banco próprio.
    /// </param>
    Task<McpToolSet> ResolveAsync(AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken);
}
