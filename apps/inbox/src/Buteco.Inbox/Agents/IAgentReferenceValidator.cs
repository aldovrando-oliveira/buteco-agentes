namespace Buteco.Inbox.Agents;

public interface IAgentReferenceValidator
{
    Task<AgentReferenceValidationResult> ValidateAsync(Guid agentId, CancellationToken cancellationToken);
}
