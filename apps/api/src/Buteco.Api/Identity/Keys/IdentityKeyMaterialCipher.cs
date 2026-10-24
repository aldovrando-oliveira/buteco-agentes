using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Identity.Keys;

/// <summary>
/// AES-256-GCM do material privado das chaves do emissor (design.md da change
/// emissor-oidc-api, D4). Formato gravado: <c>nonce (12 bytes) || ciphertext || tag
/// (16 bytes)</c>, o mesmo do <c>AesGcmMcpCredentialCipher</c>.
///
/// <para>
/// <b>Classe própria, e não o cipher de MCP generalizado:</b> é o segundo consumidor de
/// AES-GCM no <c>apps/api</c>, e a convenção 2 espera o terceiro. Cada um tem a própria
/// chave e a própria mensagem de erro.
/// </para>
/// </summary>
public sealed class IdentityKeyMaterialCipher
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    public const int KeySizeBytes = 32;

    private readonly byte[] _key;

    public IdentityKeyMaterialCipher(IOptions<IdentityIssuerOptions> options)
    {
        _key = ParseKey(options.Value.SigningKeyEncryptionKey);
    }

    /// <summary>
    /// Lê e confere a chave. Usada também pela checagem de boot, para o erro de
    /// configuração aparecer na subida e não na primeira requisição.
    /// </summary>
    public static byte[] ParseKey(string? configuredKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(
                "Identity:SigningKeyEncryptionKey não configurado, e o emissor OIDC está ligado (PublicUrl:BaseUrl presente). Gere uma chave AES-256 em base64 (`openssl rand -base64 32`), distinta das outras chaves, e configure via variável de ambiente Identity__SigningKeyEncryptionKey — nunca no banco.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Identity:SigningKeyEncryptionKey não é uma string base64 válida.", exception);
        }

        if (key.Length != KeySizeBytes)
        {
            throw new InvalidOperationException(
                $"Identity:SigningKeyEncryptionKey deve decodificar para {KeySizeBytes} bytes (AES-256); decodificou para {key.Length} bytes.");
        }

        return key;
    }

    public byte[] Protect(ReadOnlySpan<byte> material)
    {
        var stored = new byte[NonceSizeBytes + material.Length + TagSizeBytes];
        var nonce = stored.AsSpan(0, NonceSizeBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Encrypt(
            nonce,
            material,
            stored.AsSpan(NonceSizeBytes, material.Length),
            stored.AsSpan(NonceSizeBytes + material.Length, TagSizeBytes));

        return stored;
    }

    /// <summary>
    /// Lança <see cref="CryptographicException"/> se a chave configurada não for a
    /// que cifrou. Quem chama não substitui a chave gravada (D4): o anel não carrega
    /// e o emissor fica em <c>503</c>.
    /// </summary>
    public byte[] Unprotect(byte[] stored)
    {
        if (stored.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Material gravado é curto demais para conter nonce, ciphertext e tag.");
        }

        var ciphertextLength = stored.Length - NonceSizeBytes - TagSizeBytes;
        var material = new byte[ciphertextLength];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Decrypt(
            stored.AsSpan(0, NonceSizeBytes),
            stored.AsSpan(NonceSizeBytes, ciphertextLength),
            stored.AsSpan(NonceSizeBytes + ciphertextLength, TagSizeBytes),
            material);

        return material;
    }
}
