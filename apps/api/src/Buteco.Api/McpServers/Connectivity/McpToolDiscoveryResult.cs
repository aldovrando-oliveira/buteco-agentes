namespace Buteco.Api.McpServers.Connectivity;

public sealed record McpToolDiscoveryResult(bool Success, IReadOnlyList<McpToolDescriptor>? Tools, McpConnectionTestFailureReason? FailureReason, string? Message)
{
    public static McpToolDiscoveryResult Successful(IReadOnlyList<McpToolDescriptor> tools) => new(true, tools, null, null);

    public static McpToolDiscoveryResult Failed(McpConnectionTestFailureReason reason, string message) => new(false, null, reason, message);
}
