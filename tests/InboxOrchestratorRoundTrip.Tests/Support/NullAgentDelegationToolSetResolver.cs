using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents.Entities;
using Buteco.Workers.Infrastructure;
using Microsoft.Extensions.AI;

namespace InboxOrchestratorRoundTrip.Tests.Support;

// Mesmo papel de Buteco.Workers.Tests.Support.NullAgentDelegationToolSetResolver
// — delegação não faz parte do escopo desta fatia.
//
// Este projeto não está em nenhum .sln: compilar a solução de apps/workers não
// o alcança. Mudança de assinatura na interface se confere compilando TODOS os
// .csproj do repositório — foi assim que a de `sourceTaskId` (0f8dede) deixou
// este duplo sem compilar por três changes.
public sealed class NullAgentDelegationToolSetResolver : IAgentDelegationToolSetResolver
{
    public Task<IReadOnlyList<AITool>> ResolveAsync(
        AppDbContext dbContext, Agent sourceAgent, string sourceTaskId, string contextId, int currentDepth, DateTimeOffset? messageInstant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AITool>>([]);
}
