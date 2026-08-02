using System.Text.Json.Serialization;

namespace Buteco.Api.McpServers.Connectivity;

[JsonConverter(typeof(JsonStringEnumConverter<McpConnectionTestFailureReason>))]
public enum McpConnectionTestFailureReason
{
    HostUnreachable,
    CredentialRejected,

    // IMcpCredentialCipher.Decrypt lançou ao ler a credencial persistida —
    // tipicamente a chave de criptografia configurada mudou desde que a
    // credencial foi salva (ver Decision 2 do design.md da change
    // backend-mcp-catalogo-vinculo). Causa é local, distinta de
    // CredentialRejected (que é rejeição reportada pelo próprio servidor MCP).
    CredentialDecryptionFailed,
    Unknown,
}
