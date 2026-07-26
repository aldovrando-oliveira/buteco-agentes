using Buteco.Api.Agents.Responses;
using Mediator;

namespace Buteco.Api.Agents.Queries.ListAgents;

public sealed record ListAgentsQuery : IQuery<IReadOnlyList<AgentResponse>>;
