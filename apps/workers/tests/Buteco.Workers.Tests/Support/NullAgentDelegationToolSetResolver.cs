using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents.Entities;
using Buteco.Workers.Infrastructure;
using Microsoft.Extensions.AI;

namespace Buteco.Workers.Tests.Support;

/// <summary>
/// <see cref="IAgentDelegationToolSetResolver"/> que sempre resolve para
/// nenhuma tool — usado nos testes que não exercitam delegação (a maioria
/// dos testes de <see cref="Buteco.Workers.Agents.AgentExecutionService"/>),
/// mesmo papel de <see cref="NullMcpToolSetResolver"/>. Cobertura real fica
/// em <c>AgentDelegationExecutionTests.cs</c> e
/// <c>AgentDelegationConcurrencyTests.cs</c>.
/// </summary>
public sealed class NullAgentDelegationToolSetResolver : IAgentDelegationToolSetResolver
{
    public Task<IReadOnlyList<AITool>> ResolveAsync(
        AppDbContext dbContext, Agent sourceAgent, string contextId, int currentDepth, DateTimeOffset? messageInstant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AITool>>([]);
}
