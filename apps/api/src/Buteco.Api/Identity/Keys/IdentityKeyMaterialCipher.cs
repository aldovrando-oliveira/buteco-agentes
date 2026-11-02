using System.Security.Cryptography;
using Buteco.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Identity.Keys;

/// <summary>
/// AES-256-GCM do material privado das chaves do emissor (design.md da change
/// emissor-oidc-api, D4), pelo <see cref="AesGcmEnvelope"/>: <c>nonce (12 bytes) ||
/// ciphertext || tag (16 bytes)</c>, com a própria chave e as próprias mensagens.
/// </summary>
public sealed class IdentityKeyMaterialCipher
{
    public const int KeySizeBytes = AesGcmEnvelope.KeySizeBytes;

    private readonly byte[] _key;

    public IdentityKeyMaterialCipher(IOptions<IdentityIssuerOptions> options)
    {
        _key = ParseKey(options.Value.SigningKeyEncryptionKey);
    }

    /// <summary>
    /// Lê e confere a chave. Usada também pela checagem de boot, para o erro de
    /// configuração aparecer na subida e não na primeira requisição.
    /// </summary>
    public static byte[] ParseKey(string? configuredKey) => AesGcmEnvelope.ParseKey(
        "Identity:SigningKeyEncryptionKey",
        configuredKey,
        "Identity:SigningKeyEncryptionKey não configurado, e o emissor OIDC está ligado (PublicUrl:BaseUrl presente). Gere uma chave AES-256 em base64 (`openssl rand -base64 32`), distinta das outras chaves, e configure via variável de ambiente Identity__SigningKeyEncryptionKey — nunca no banco.");

    public byte[] Protect(ReadOnlySpan<byte> material) => AesGcmEnvelope.Seal(_key, material);

    /// <summary>
    /// Lança <see cref="CryptographicException"/> se a chave configurada não for a
    /// que cifrou. Quem chama não substitui a chave gravada (D4): o anel não carrega
    /// e o emissor fica em <c>503</c>.
    /// </summary>
    public byte[] Unprotect(byte[] stored) =>
        AesGcmEnvelope.Open(_key, stored, "Material gravado é curto demais para conter nonce, ciphertext e tag.");
}
