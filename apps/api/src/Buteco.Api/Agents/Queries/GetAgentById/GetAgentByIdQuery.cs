using Buteco.Api.Agents.Responses;
using Mediator;

namespace Buteco.Api.Agents.Queries.GetAgentById;

public sealed record GetAgentByIdQuery(Guid Id) : IQuery<AgentResponse?>;
