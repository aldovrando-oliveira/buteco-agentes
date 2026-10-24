using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Api.Identity.Keys;

/// <summary>
/// Gera as chaves do emissor e monta a linha de <c>identity_keys</c> (design.md da
/// change emissor-oidc-api, D4): RSA 2048 para assinar, AES-256 para cifrar, com o
/// material privado já cifrado por <see cref="IdentityKeyMaterialCipher"/>.
/// </summary>
public sealed class IdentityKeyFactory(IdentityKeyMaterialCipher cipher)
{
    public const string SigningAlgorithm = SecurityAlgorithms.RsaSha256;
    public const string EncryptionAlgorithm = SecurityAlgorithms.Aes256KW;

    private const int RsaKeySizeBits = 2048;
    private const int AesKeySizeBytes = 32;

    public IdentityKey CreateSigningKey(IdentityKeyWindows windows, DateTimeOffset now)
    {
        using var rsa = RSA.Create(RsaKeySizeBits);
        var pkcs8 = rsa.ExportPkcs8PrivateKey();
        try
        {
            return Row(IdentityKeyUsage.Signing, SigningAlgorithm, pkcs8, windows, now);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    public IdentityKey CreateEncryptionKey(IdentityKeyWindows windows, DateTimeOffset now)
    {
        var material = RandomNumberGenerator.GetBytes(AesKeySizeBytes);
        try
        {
            return Row(IdentityKeyUsage.Encryption, EncryptionAlgorithm, material, windows, now);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private IdentityKey Row(
        IdentityKeyUsage usage,
        string algorithm,
        byte[] material,
        IdentityKeyWindows windows,
        DateTimeOffset now) => new()
    {
        // Aleatório, e não o thumbprint que o OpenIddict derivaria do certificado em
        // memória: o certificado é refeito a cada carga (D5), o kid é da linha.
        Kid = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16)),
        Usage = usage,
        Algorithm = algorithm,
        ProtectedMaterial = cipher.Protect(material),
        CreatedAt = now,
        SignsFrom = windows.SignsFrom,
        SignsUntil = windows.SignsUntil,
        ValidatesUntil = windows.ValidatesUntil,
    };
}
