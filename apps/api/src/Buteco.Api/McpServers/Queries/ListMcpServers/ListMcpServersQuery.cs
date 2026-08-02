using Buteco.Api.McpServers.Responses;
using Mediator;

namespace Buteco.Api.McpServers.Queries.ListMcpServers;

public sealed record ListMcpServersQuery : IQuery<IReadOnlyList<McpServerResponse>>;
