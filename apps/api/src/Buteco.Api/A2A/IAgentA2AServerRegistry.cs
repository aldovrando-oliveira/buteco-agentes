using global::A2A;

namespace Buteco.Api.A2A;

public interface IAgentA2AServerRegistry
{
    void Register(Guid agentId);

    Task<A2AServer?> GetOrCreateAsync(Guid agentId, CancellationToken cancellationToken);
}
