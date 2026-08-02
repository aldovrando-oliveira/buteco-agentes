using Buteco.Api.McpServers.Entities;

namespace Buteco.Api.McpServers.Connectivity;

/// <summary>
/// Executa o handshake MCP real (mensagem <c>initialize</c>) contra um
/// servidor remoto. Interface existe para permitir substituição em teste,
/// mesmo espírito de <c>IChatClientResolver</c> em apps/workers — construído
/// por chamada, sem cache entre chamadas.
/// </summary>
public interface IMcpConnectionTester
{
    Task<McpConnectionTestResult> TestAsync(string url, McpServerAuthType authType, string? credential, CancellationToken cancellationToken);
}
