using System.Security.Cryptography;
using Buteco.Inbox.Channels.Security;

namespace Buteco.Inbox.Tests;

public class AesGcmChannelCredentialCipherTests
{
    private static AesGcmChannelCredentialCipher CreateCipher(string? key = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=") =>
        new(Microsoft.Extensions.Options.Options.Create(new InboxCryptoOptions { CredentialEncryptionKey = key }));

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
