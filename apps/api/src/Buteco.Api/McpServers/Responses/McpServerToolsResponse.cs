using Buteco.Api.McpServers.Connectivity;

namespace Buteco.Api.McpServers.Responses;

/// <summary>
/// Corpo de <c>GET /mcp-servers/{id}/tools</c> — mesmo espírito de
/// <c>McpConnectionTestResult</c>: resultado efêmero (sucesso com a lista de
/// tools, ou falha com motivo), nunca persistido.
/// </summary>
public sealed record McpServerToolsResponse(bool Success, IReadOnlyList<McpServerToolResponse>? Tools, McpConnectionTestFailureReason? FailureReason, string? Message)
{
    public static McpServerToolsResponse FromDiscoveryResult(McpToolDiscoveryResult result) =>
        result.Success
            ? new(true, result.Tools!.Select(McpServerToolResponse.FromDescriptor).ToList(), null, null)
            : new(false, null, result.FailureReason, result.Message);
}
