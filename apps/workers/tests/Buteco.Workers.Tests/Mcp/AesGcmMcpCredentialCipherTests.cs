using System.Security.Cryptography;
using Buteco.Workers.Mcp.Security;

namespace Buteco.Workers.Tests.Mcp;

/// <summary>
/// Mirror dos testes já existentes em <c>apps/api</c>
/// (<c>AesGcmMcpCredentialCipherTests.cs</c>), adaptado ao namespace de
/// <c>apps/workers</c> — mesmo algoritmo, mesmo contrato.
/// </summary>
public class AesGcmMcpCredentialCipherTests
{
    private static AesGcmMcpCredentialCipher CreateCipher(string? key = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=") =>
        new(Microsoft.Extensions.Options.Options.Create(new McpCryptoOptions { CredentialEncryptionKey = key }));

    [Fact]
    public void EncryptThenDecrypt_ReturnsOriginalPlaintext()
    {
        var cipher = CreateCipher();

        var stored = cipher.Encrypt("super-secret-token");

        Assert.Equal("super-secret-token", cipher.Decrypt(stored));
    }

    [Fact]
    public void Encrypt_SamePlaintextTwice_ProducesDifferentStoredValues()
    {
        var cipher = CreateCipher();

        var first = cipher.Encrypt("super-secret-token");
        var second = cipher.Encrypt("super-secret-token");

        Assert.NotEqual(first, second);
        Assert.Equal("super-secret-token", cipher.Decrypt(first));
        Assert.Equal("super-secret-token", cipher.Decrypt(second));
    }

    [Fact]
    public void Decrypt_CorruptedStoredValue_Throws()
    {
        var cipher = CreateCipher();
        var stored = cipher.Encrypt("super-secret-token");
        var corrupted = stored[..^4] + "abcd";

        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(corrupted));
    }

    [Fact]
    public void Decrypt_ValueEncryptedWithDifferentKey_Throws()
    {
        var cipherA = CreateCipher("NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=");
        var cipherB = CreateCipher("YWxsLXplcm9lcy1rZXktZm9yLXRlc3Rpbmctb25seS0=");

        var storedWithKeyA = cipherA.Encrypt("super-secret-token");

        Assert.ThrowsAny<CryptographicException>(() => cipherB.Decrypt(storedWithKeyA));
    }

    [Fact]
    public void Constructor_MissingKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateCipher(key: null));
    }

    [Fact]
    public void Constructor_KeyWithWrongLength_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateCipher(key: Convert.ToBase64String(new byte[16])));
    }
}
