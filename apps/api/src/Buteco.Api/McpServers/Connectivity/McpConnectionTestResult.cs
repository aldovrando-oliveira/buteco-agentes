namespace Buteco.Api.McpServers.Connectivity;

public sealed record McpConnectionTestResult(bool Success, McpConnectionTestFailureReason? FailureReason, string? Message)
{
    public static McpConnectionTestResult Successful() => new(true, null, null);

    public static McpConnectionTestResult Failed(McpConnectionTestFailureReason reason, string message) => new(false, reason, message);
}
