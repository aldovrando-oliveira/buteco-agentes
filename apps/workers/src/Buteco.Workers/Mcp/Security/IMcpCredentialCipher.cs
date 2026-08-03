namespace Buteco.Workers.Mcp.Security;

/// <summary>
/// Decifra a credencial persistida de <see cref="Entities.McpServer"/> antes
/// de usá-la para conectar de verdade (mirror de <c>apps/api</c> —
/// <c>Mcp:CredentialEncryptionKey</c> é a mesma chave, compartilhada entre os
/// dois apps, para que uma credencial cifrada por <c>apps/api</c> seja
/// decifrável por <c>apps/workers</c>). Interface existe para não acoplar o
/// restante do código à escolha de algoritmo e para permitir teste
/// determinístico.
/// </summary>
public interface IMcpCredentialCipher
{
    string Encrypt(string plaintext);

    string Decrypt(string stored);
}
