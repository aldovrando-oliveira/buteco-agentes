using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;

namespace InboxOrchestratorRoundTrip.Tests.Support;

// Mesmo papel de Buteco.Workers.Tests.Support.NullMcpToolSetResolver — MCP
// não faz parte do escopo desta fatia (design.md, Non-Goals), então o
// worker real usado no round-trip não precisa de nenhuma conexão MCP de
// verdade.
public sealed class NullMcpToolSetResolver : IMcpToolSetResolver
{
    public Task<McpToolSet> ResolveAsync(AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken) =>
        Task.FromResult(new McpToolSet([], []));
}
