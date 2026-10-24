using System.Security.Cryptography;
using Buteco.Api.Identity;
using Buteco.Api.Identity.Keys;

namespace Buteco.Api.Tests.Identity;

public class IdentityKeyMaterialCipherTests
{
    private const string Key = "dGVzdC1pZGVudGl0eS1rZXktMzItYnl0ZXMtLS0tLS0=";
    private const string OtherKey = "b3V0cmEtY2hhdmUtZGUtMzItYnl0ZXMtLS0tLS0tLS0=";

    private static IdentityKeyMaterialCipher CreateCipher(string? key = Key) =>
        new(Microsoft.Extensions.Options.Options.Create(new IdentityIssuerOptions { SigningKeyEncryptionKey = key }));

    [Fact]
    public void ProtectThenUnprotect_ReturnsTheOriginalMaterial()
    {
        var cipher = CreateCipher();
        var material = RandomNumberGenerator.GetBytes(1200);

        Assert.Equal(material, cipher.Unprotect(cipher.Protect(material)));
    }

    [Fact]
    public void Protect_SameMaterialTwice_UsesADistinctNonce()
    {
        var cipher = CreateCipher();
        var material = RandomNumberGenerator.GetBytes(32);

        var first = cipher.Protect(material);
        var second = cipher.Protect(material);

        Assert.NotEqual(first[..12], second[..12]);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Unprotect_WithTheWrongKey_Throws()
    {
        var stored = CreateCipher().Protect(RandomNumberGenerator.GetBytes(32));

        Assert.ThrowsAny<CryptographicException>(() => CreateCipher(OtherKey).Unprotect(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingKey_FailsNamingTheSetting(string? key)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateCipher(key));

        Assert.Contains("Identity:SigningKeyEncryptionKey não configurado", exception.Message);
    }

    [Fact]
    public void KeyOutsideBase64_FailsNamingTheSetting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateCipher("isto não é base64!"));

        Assert.Equal("Identity:SigningKeyEncryptionKey não é uma string base64 válida.", exception.Message);
    }

    [Fact]
    public void KeyWithTheWrongSize_FailsNamingTheSettingAndTheSize()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateCipher(Convert.ToBase64String(new byte[16])));

        Assert.Equal(
            "Identity:SigningKeyEncryptionKey deve decodificar para 32 bytes (AES-256); decodificou para 16 bytes.",
            exception.Message);
    }
}
