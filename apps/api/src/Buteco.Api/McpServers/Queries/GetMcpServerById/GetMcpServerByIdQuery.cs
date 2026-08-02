using Buteco.Api.McpServers.Responses;
using Mediator;

namespace Buteco.Api.McpServers.Queries.GetMcpServerById;

public sealed record GetMcpServerByIdQuery(Guid Id) : IQuery<McpServerResponse?>;
