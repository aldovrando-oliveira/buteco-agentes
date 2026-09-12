using Buteco.Api.McpServers.Entities;

namespace Buteco.Api.McpServers.Connectivity;

/// <summary>
/// Executa o handshake MCP real (mensagem <c>initialize</c>) contra um
/// servidor remoto, e a descoberta de tools (<c>tools/list</c>) do mesmo
/// servidor. Interface existe para permitir substituição em teste.
///
/// <para>
/// Esta docstring citava o ciclo de vida de <c>IChatClientResolver</c> em
/// apps/workers como precedente ("construído por chamada, sem cache"). A citação
/// foi REMOVIDA, não atualizada: aquele componente passou a cachear por
/// <c>(provider, model)</c> e a frase virou mentira em silêncio, sem que nada
/// aqui quebrasse. Afirmação sobre o ciclo de vida de código de OUTRO app decai
/// sem deixar rastro — nem o compilador nem scripts/check-docs.py alcançam.
/// </para>
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
