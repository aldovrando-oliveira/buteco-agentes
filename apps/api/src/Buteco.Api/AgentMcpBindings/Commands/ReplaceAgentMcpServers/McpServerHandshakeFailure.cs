using Buteco.Api.McpServers.Connectivity;

namespace Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;

/// <summary>
/// Falha do handshake de validação (`tools/list`) contra um `McpServer`
/// referenciado no payload de `PUT /agents/{id}/mcp-servers` — rejeita a
/// operação inteira com HTTP 502 (Decision 6 do design.md da change
/// backend-mcp-selecao-tools), distinto de tool inexistente (400).
/// </summary>
public sealed record McpServerHandshakeFailure(Guid McpServerId, McpConnectionTestFailureReason Reason, string Message);
