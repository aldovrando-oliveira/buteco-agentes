using Mediator;

namespace Buteco.Api.McpServers.Queries.ListMcpServerTools;

public sealed record ListMcpServerToolsQuery(Guid Id) : IQuery<ListMcpServerToolsResult>;
