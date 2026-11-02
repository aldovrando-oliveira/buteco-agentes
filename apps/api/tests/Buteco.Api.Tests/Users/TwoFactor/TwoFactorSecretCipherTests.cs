using System.Security.Cryptography;
using Buteco.Api.Identity;
using Buteco.Api.Users.TwoFactor;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// O segredo do TOTP em repouso (spec <c>two-factor-totp</c>, "Segredo cifrado em repouso com
/// chave própria"; design.md da change verificacao-2-etapas-totp, D5).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 4.3: sem o dado
/// associado, <see cref="SecretCopiedToAnotherUser_DoesNotOpen"/> reprova.
/// </para>
/// </summary>
public class TwoFactorSecretCipherTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    private static readonly string OtherKey = Convert.ToBase64String(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray());

    private static TwoFactorSecretCipher Cipher(string key) =>
        new(Microsoft.Extensions.Options.Options.Create(new IdentityIssuerOptions { TwoFactorEncryptionKey = key }));

    [Fact]
    public void RoundTrip_AndTheStoredValueIsNotTheSecret()
    {
        var userId = Guid.NewGuid();
        var secret = Totp.NewSecret();

        var stored = Cipher(Key).Protect(userId, secret);

        Assert.Equal(secret, Cipher(Key).Unprotect(userId, stored));
        Assert.False(stored.AsSpan().IndexOf(secret) >= 0);
        Assert.DoesNotContain(Base32.Encode(secret), System.Text.Encoding.ASCII.GetString(stored));
    }

    [Fact]
    public void SecretCopiedToAnotherUser_DoesNotOpen()
    {
        var stored = Cipher(Key).Protect(Guid.NewGuid(), Totp.NewSecret());

        Assert.ThrowsAny<CryptographicException>(() => Cipher(Key).Unprotect(Guid.NewGuid(), stored));
    }

    [Fact]
    public void WrongKey_Throws()
    {
        var userId = Guid.NewGuid();
        var stored = Cipher(Key).Protect(userId, Totp.NewSecret());

        Assert.ThrowsAny<CryptographicException>(() => Cipher(OtherKey).Unprotect(userId, stored));
    }

    [Fact]
    public void MissingKey_NamesTheConfiguration()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Cipher(""));

        Assert.StartsWith("Identity:TwoFactorEncryptionKey não configurado.", exception.Message);
    }
}
