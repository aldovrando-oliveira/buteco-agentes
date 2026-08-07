using Mediator;

namespace Buteco.Api.AgentDelegations.Commands.ReplaceAgentDelegations;

public sealed record ReplaceAgentDelegationsCommand(Guid AgentId, IReadOnlyList<Guid> TargetAgentIds) : ICommand<ReplaceAgentDelegationsResult>;
