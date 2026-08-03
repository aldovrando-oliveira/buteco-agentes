using Buteco.Api.McpServers.Entities;

namespace Buteco.Api.McpServers.Connectivity;

/// <summary>
/// Executa o handshake MCP real (mensagem <c>initialize</c>) contra um
/// servidor remoto, e a descoberta de tools (<c>tools/list</c>) do mesmo
/// servidor. Interface existe para permitir substituição em teste, mesmo
/// espírito de <c>IChatClientResolver</c> em apps/workers — construído por
/// chamada, sem cache entre chamadas.
/// </summary>
public interface IMcpConnectionTester
{
    Task<McpConnectionTestResult> TestAsync(string url, McpServerAuthType authType, string? credential, CancellationToken cancellationToken);

    // Descoberta ao vivo das tools oferecidas pelo servidor (Decision 3 do
    // design.md da change backend-mcp-selecao-tools) — reaproveita a mesma
    // construção de transporte de TestAsync, sem duplicar lógica de conexão.
    // Usada tanto por GET /mcp-servers/{id}/tools quanto pela validação de
    // PUT /agents/{id}/mcp-servers.
    Task<McpToolDiscoveryResult> ListToolsAsync(string url, McpServerAuthType authType, string? credential, CancellationToken cancellationToken);
}
