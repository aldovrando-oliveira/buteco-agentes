using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;

namespace Buteco.Workers.Tests.Support;

/// <summary>
/// <see cref="IMcpToolSetResolver"/> que sempre resolve para um
/// <see cref="McpToolSet"/> vazio (nenhuma tool, nenhuma conexão) — usado nos
/// testes que não exercitam MCP (a maioria dos testes de
/// <see cref="Buteco.Workers.Agents.AgentExecutionService"/>), para não
/// precisar montar toda a infraestrutura real de conexão MCP só para
/// satisfazer a dependência do construtor. Cobertura real de
/// <see cref="IMcpToolSetResolver"/> fica em
/// <c>Mcp/McpToolSetResolverTests.cs</c> e
/// <c>Mcp/McpToolExecutionEndToEndTests.cs</c>.
/// </summary>
public sealed class NullMcpToolSetResolver : IMcpToolSetResolver
{
    public Task<McpToolSet> ResolveAsync(AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken) =>
        Task.FromResult(new McpToolSet([], []));
}
