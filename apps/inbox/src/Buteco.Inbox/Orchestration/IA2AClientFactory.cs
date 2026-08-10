using A2A;

namespace Buteco.Inbox.Orchestration;

public interface IA2AClientFactory
{
    IA2AClient CreateForAgent(Guid agentId);
}
