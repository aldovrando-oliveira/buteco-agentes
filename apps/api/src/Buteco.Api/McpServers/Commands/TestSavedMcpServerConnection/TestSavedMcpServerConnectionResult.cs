using Buteco.Api.McpServers.Connectivity;

namespace Buteco.Api.McpServers.Commands.TestSavedMcpServerConnection;

public sealed record TestSavedMcpServerConnectionResult(McpConnectionTestResult? Result, bool Found)
{
    public static TestSavedMcpServerConnectionResult NotFound() => new(null, Found: false);

    public static TestSavedMcpServerConnectionResult Completed(McpConnectionTestResult result) => new(result, Found: true);
}
