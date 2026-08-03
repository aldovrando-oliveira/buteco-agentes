using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Buteco.Workers.Mcp.Security;

/// <summary>
/// Mirror exato do algoritmo de <c>apps/api</c>
/// (<c>McpServers/Security/AesGcmMcpCredentialCipher.cs</c>): AES-GCM com
/// nonce aleatório de 96 bits por chamada, chave única de 256 bits vinda de
/// configuração/env var (nunca do banco). Formato persistido: base64 de
/// <c>nonce (12 bytes) || ciphertext || tag (16 bytes)</c>.
/// </summary>
public sealed class AesGcmMcpCredentialCipher : IMcpCredentialCipher
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32;

    private readonly byte[] _key;

    public AesGcmMcpCredentialCipher(IOptions<McpCryptoOptions> options)
    {
        var configuredKey = options.Value.CredentialEncryptionKey;
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(
                "Mcp:CredentialEncryptionKey não configurado. Gere uma chave AES-256 em base64 (ex.: `openssl rand -base64 32`) e configure via variável de ambiente/configuração — nunca no banco.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Mcp:CredentialEncryptionKey não é uma string base64 válida.", exception);
        }

        if (key.Length != KeySizeBytes)
        {
            throw new InvalidOperationException(
                $"Mcp:CredentialEncryptionKey deve decodificar para {KeySizeBytes} bytes (AES-256); decodificou para {key.Length} bytes.");
        }

        _key = key;
    }

    public string Encrypt(string plaintext)
    {
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using (var aesGcm = new AesGcm(_key, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var stored = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        nonce.CopyTo(stored, 0);
        ciphertext.CopyTo(stored, NonceSizeBytes);
        tag.CopyTo(stored, NonceSizeBytes + ciphertext.Length);

        return Convert.ToBase64String(stored);
    }

    public string Decrypt(string stored)
    {
        byte[] storedBytes;
        try
        {
            storedBytes = Convert.FromBase64String(stored);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("Valor persistido não é uma string base64 válida.", exception);
        }

        if (storedBytes.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Valor persistido é curto demais para conter nonce, ciphertext e tag.");
        }

        var nonce = storedBytes[..NonceSizeBytes];
        var tag = storedBytes[^TagSizeBytes..];
        var ciphertext = storedBytes[NonceSizeBytes..^TagSizeBytes];
        var plaintextBytes = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        return System.Text.Encoding.UTF8.GetString(plaintextBytes);
    }
}
