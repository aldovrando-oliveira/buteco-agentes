namespace Buteco.Api.AgentDelegations.Requests;

public sealed record ReplaceAgentDelegationsRequest(IReadOnlyList<Guid>? TargetAgentIds);
