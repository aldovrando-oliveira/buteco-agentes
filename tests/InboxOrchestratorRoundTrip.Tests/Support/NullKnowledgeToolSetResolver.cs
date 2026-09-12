using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Execution;
using Microsoft.Extensions.AI;

namespace InboxOrchestratorRoundTrip.Tests.Support;

// Mesmo papel de Buteco.Workers.Tests.Support.NullKnowledgeToolSetResolver
// — conhecimento não faz parte do escopo desta fatia. Duplicado de propósito:
// os dois projetos de teste não se referenciam, mesmo padrão dos outros dois
// nulos ao lado.
public sealed class NullKnowledgeToolSetResolver : IKnowledgeToolSetResolver
{
    public Task<IReadOnlyList<AITool>> ResolveAsync(AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AITool>>([]);
}
