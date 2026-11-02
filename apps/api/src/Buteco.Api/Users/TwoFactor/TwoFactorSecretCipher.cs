using System.Security.Cryptography;
using Buteco.Api.Identity;
using Buteco.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// O segredo do TOTP em repouso (design.md da change verificacao-2-etapas-totp, D5):
/// AES-256-GCM pelo <see cref="AesGcmEnvelope"/>, com <c>Identity:TwoFactorEncryptionKey</c>
/// e o id do usuário como dado associado. Copiar o segredo cifrado para a linha de outro
/// usuário faz a decifração falhar.
/// </summary>
public sealed class TwoFactorSecretCipher
{
    public const string ConfigurationName = "Identity:TwoFactorEncryptionKey";

    private readonly byte[] _key;

    public TwoFactorSecretCipher(IOptions<IdentityIssuerOptions> options)
    {
        _key = ParseKey(options.Value.TwoFactorEncryptionKey);
    }

    /// <summary>Lê e confere a chave; usada também pela checagem de boot.</summary>
    public static byte[] ParseKey(string? configuredKey) => AesGcmEnvelope.ParseKey(
        ConfigurationName,
        configuredKey,
        $"{ConfigurationName} não configurado. Gere uma chave AES-256 em base64 (`openssl rand -base64 32`), distinta das outras chaves, e configure via variável de ambiente Identity__TwoFactorEncryptionKey — nunca no banco.");

    public byte[] Protect(Guid userId, ReadOnlySpan<byte> secret) => AesGcmEnvelope.Seal(_key, secret, userId.ToByteArray());

    /// <summary>
    /// Lança <see cref="CryptographicException"/> se a chave configurada não for a que cifrou,
    /// ou se o segredo for de outro usuário.
    /// </summary>
    public byte[] Unprotect(Guid userId, byte[] stored) =>
        AesGcmEnvelope.Open(_key, stored, "Segredo gravado é curto demais para conter nonce, ciphertext e tag.", userId.ToByteArray());
}
