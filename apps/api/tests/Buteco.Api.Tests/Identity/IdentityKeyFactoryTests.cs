using System.Security.Cryptography;
using System.Text;
using Buteco.Api.Identity;
using Buteco.Api.Identity.Keys;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// A linha que a fábrica monta (design.md da change emissor-oidc-api, D4): o material
/// gravado nunca é a chave legível, em nenhum dos formatos em que uma chave RSA costuma
/// vazar.
/// </summary>
public class IdentityKeyFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly IdentityKeyMaterialCipher Cipher = new(Microsoft.Extensions.Options.Options.Create(
        new IdentityIssuerOptions { SigningKeyEncryptionKey = "dGVzdC1pZGVudGl0eS1rZXktMzItYnl0ZXMtLS0tLS0=" }));

    // OID rsaEncryption (1.2.840.113549.1.1.1) codificado em DER: está em todo PKCS#8 de RSA.
    private static readonly byte[] RsaEncryptionOid = [0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01];

    [Fact]
    public void SigningKey_StoredMaterial_IsNotPkcs8_PemNorJwk_AndDecryptsToAnRsa2048Key()
    {
        var factory = new IdentityKeyFactory(Cipher);

        var row = factory.CreateSigningKey(IdentityKeyRotationPolicy.FirstSigningKey(Now), Now);

        Assert.Equal(IdentityKeyUsage.Signing, row.Usage);
        Assert.Equal("RS256", row.Algorithm);
        Assert.False(Contains(row.ProtectedMaterial, RsaEncryptionOid), "o material gravado contém o OID de um PKCS#8 RSA");

        var asText = Encoding.UTF8.GetString(row.ProtectedMaterial);
        Assert.DoesNotContain("BEGIN", asText);
        Assert.DoesNotContain("\"d\"", asText);
        Assert.DoesNotContain("\"kty\"", asText);

        var pkcs8 = Cipher.Unprotect(row.ProtectedMaterial);
        Assert.True(Contains(pkcs8, RsaEncryptionOid));
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(pkcs8, out _);
        Assert.Equal(2048, rsa.KeySize);
    }

    [Fact]
    public void EncryptionKey_StoresA256BitKey_Encrypted()
    {
        var factory = new IdentityKeyFactory(Cipher);

        var row = factory.CreateEncryptionKey(IdentityKeyRotationPolicy.EncryptionKey(Now), Now);

        Assert.Equal(IdentityKeyUsage.Encryption, row.Usage);
        Assert.Equal("A256KW", row.Algorithm);
        Assert.Equal(32, Cipher.Unprotect(row.ProtectedMaterial).Length);
        Assert.Equal(12 + 32 + 16, row.ProtectedMaterial.Length);
    }

    [Fact]
    public void Kid_IsRandom_PerKey()
    {
        var factory = new IdentityKeyFactory(Cipher);
        var windows = IdentityKeyRotationPolicy.EncryptionKey(Now);

        var kids = Enumerable.Range(0, 20).Select(_ => factory.CreateEncryptionKey(windows, Now).Kid).ToHashSet();

        Assert.Equal(20, kids.Count);
        Assert.All(kids, kid => Assert.Equal(22, kid.Length));
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}
