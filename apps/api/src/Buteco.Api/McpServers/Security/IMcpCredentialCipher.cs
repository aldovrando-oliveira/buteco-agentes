namespace Buteco.Api.McpServers.Security;

/// <summary>
/// Criptografa/decifra a credencial de <see cref="Entities.McpServer"/> antes
/// de persistir/depois de ler do banco (Decision 1 do design.md da change
/// backend-mcp-catalogo-vinculo — AES-GCM manual com chave única de
/// configuração, não Data Protection API). Interface existe para não acoplar
/// o restante do código à escolha de algoritmo e para permitir teste
/// determinístico.
/// </summary>
public interface IMcpCredentialCipher
{
    string Encrypt(string plaintext);

    string Decrypt(string stored);
}
