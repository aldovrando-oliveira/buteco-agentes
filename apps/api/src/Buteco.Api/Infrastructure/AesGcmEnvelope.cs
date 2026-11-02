using System.Security.Cryptography;

namespace Buteco.Api.Infrastructure;

/// <summary>
/// AES-256-GCM em repouso, no formato <c>nonce (12 bytes) || ciphertext || tag (16
/// bytes)</c>, com nonce aleatório por chamada (design.md da change
/// verificacao-2-etapas-totp, D5). Extraído no terceiro consumidor, como a convenção 2 pede:
/// as credenciais de MCP, o material das chaves do emissor e o segredo do TOTP. Cada um tem a
/// própria chave e as próprias mensagens; o formato é o mesmo, e a extração não o mudou.
///
/// <para>
/// O <c>apps/workers</c> tem a própria cópia do cifrador de MCP
/// (<c>apps/workers/src/Buteco.Workers/Mcp/Security/AesGcmMcpCredentialCipher.cs</c>), que lê
/// o que o <c>apps/api</c> grava. O acordo é o <c>McpServerCrossAppCompatibilityTests</c>. Por
/// isso o cifrador de MCP não usa dado associado.
/// </para>
/// </summary>
public static class AesGcmEnvelope
{
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;
    public const int KeySizeBytes = 32;

    /// <summary>
    /// Lê e confere uma chave de configuração. A mensagem de ausência é de quem chama; as de
    /// formato citam o nome da configuração.
    /// </summary>
    public static byte[] ParseKey(string configurationName, string? configuredKey, string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(missingMessage);
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{configurationName} não é uma string base64 válida.", exception);
        }

        if (key.Length != KeySizeBytes)
        {
            throw new InvalidOperationException(
                $"{configurationName} deve decodificar para {KeySizeBytes} bytes (AES-256); decodificou para {key.Length} bytes.");
        }

        return key;
    }

    public static byte[] Seal(byte[] key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        var stored = new byte[NonceSizeBytes + plaintext.Length + TagSizeBytes];
        var nonce = stored.AsSpan(0, NonceSizeBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Encrypt(
            nonce,
            plaintext,
            stored.AsSpan(NonceSizeBytes, plaintext.Length),
            stored.AsSpan(NonceSizeBytes + plaintext.Length, TagSizeBytes),
            associatedData);

        return stored;
    }

    /// <summary>
    /// Lança <see cref="CryptographicException"/> se o valor for curto demais, se a chave não for
    /// a que cifrou, ou se o dado associado não for o da cifragem.
    /// </summary>
    public static byte[] Open(byte[] key, ReadOnlySpan<byte> stored, string tooShortMessage, ReadOnlySpan<byte> associatedData = default)
    {
        if (stored.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException(tooShortMessage);
        }

        var ciphertextLength = stored.Length - NonceSizeBytes - TagSizeBytes;
        var plaintext = new byte[ciphertextLength];

        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Decrypt(
            stored[..NonceSizeBytes],
            stored.Slice(NonceSizeBytes, ciphertextLength),
            stored.Slice(NonceSizeBytes + ciphertextLength, TagSizeBytes),
            plaintext,
            associatedData);

        return plaintext;
    }
}
