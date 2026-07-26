using System.Collections.Concurrent;
using global::A2A;
using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.A2A;

public sealed class AgentA2AServerRegistry(
    IServiceScopeFactory scopeFactory,
    ITaskJobPublisher taskJobPublisher,
    ILoggerFactory loggerFactory) : IAgentA2AServerRegistry
{
    private readonly ConcurrentDictionary<Guid, A2AServer> _servers = new();

    public void Register(Guid agentId) => _servers.TryAdd(agentId, BuildServer(agentId));

    public async Task<A2AServer?> GetOrCreateAsync(Guid agentId, CancellationToken cancellationToken)
    {
        if (_servers.TryGetValue(agentId, out var existing))
        {
            return existing;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exists = await dbContext.Agents.AsNoTracking().AnyAsync(agent => agent.Id == agentId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return _servers.GetOrAdd(agentId, BuildServer);
    }

    private A2AServer BuildServer(Guid agentId)
    {
        var taskStore = new PostgresTaskStore(scopeFactory, agentId);
        var handler = new EnqueueingAgentHandler(agentId, taskJobPublisher);
        var notifier = new ChannelEventNotifier();
        var logger = loggerFactory.CreateLogger<A2AServer>();

        return new A2AServer(handler, taskStore, notifier, logger);
    }
}
