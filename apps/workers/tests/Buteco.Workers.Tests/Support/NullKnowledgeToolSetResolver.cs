using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Execution;
using Microsoft.Extensions.AI;

namespace Buteco.Workers.Tests.Support;

/// <summary>
/// <see cref="IKnowledgeToolSetResolver"/> que sempre resolve para nenhuma tool
/// — usado nos testes que não exercitam conhecimento (a maioria dos testes de
/// <see cref="Buteco.Workers.Agents.AgentExecutionService"/>), mesmo papel de
/// <see cref="NullMcpToolSetResolver"/> e
/// <see cref="NullAgentDelegationToolSetResolver"/>. Cobertura real fica em
/// <c>Knowledge/KnowledgeToolSetResolverTests.cs</c> e
/// <c>Knowledge/KnowledgeToolExecutionEndToEndTests.cs</c>.
/// </summary>
public sealed class NullKnowledgeToolSetResolver : IKnowledgeToolSetResolver
{
    public Task<IReadOnlyList<AITool>> ResolveAsync(AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AITool>>([]);
}
