using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents.Entities;
using Buteco.Workers.Infrastructure;
using Microsoft.Extensions.AI;

namespace InboxOrchestratorRoundTrip.Tests.Support;

// Mesmo papel de Buteco.Workers.Tests.Support.NullAgentDelegationToolSetResolver
// — delegação não faz parte do escopo desta fatia.
public sealed class NullAgentDelegationToolSetResolver : IAgentDelegationToolSetResolver
{
    public Task<IReadOnlyList<AITool>> ResolveAsync(
        AppDbContext dbContext, Agent sourceAgent, string contextId, int currentDepth, DateTimeOffset? messageInstant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AITool>>([]);
}
