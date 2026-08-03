using Buteco.Api.McpServers.Responses;

namespace Buteco.Api.McpServers.Queries.ListMcpServerTools;

public sealed record ListMcpServerToolsResult(McpServerToolsResponse? Result, bool Found)
{
    public static ListMcpServerToolsResult NotFound() => new(null, Found: false);

    public static ListMcpServerToolsResult Completed(McpServerToolsResponse result) => new(result, Found: true);
}
