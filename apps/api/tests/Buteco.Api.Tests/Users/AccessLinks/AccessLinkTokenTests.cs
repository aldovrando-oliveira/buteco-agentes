using System.Security.Cryptography;
using System.Text;
using Buteco.Api.Users.AccessLinks;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// O token do link (design.md da change convites-redefinicao-senha, D4).
///
/// <para>
/// Verificado contra o defeito (tasks.md, 3.5): o hash calculado sobre os bytes decodificados do
/// base64url, e não sobre o texto → <see cref="TheHash_IsTheSha256OfTheTokenText"/> reprova.
/// </para>
/// </summary>
public class AccessLinkTokenTests
{
    [Fact]
    public void TheToken_Has43Base64UrlCharacters_WithoutPadding()
    {
        var token = AccessLinkToken.New();

        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
    }

    [Fact]
    public void TwoTokens_Differ()
    {
        Assert.NotEqual(AccessLinkToken.New(), AccessLinkToken.New());
    }

    [Fact]
    public void TheHash_IsTheSha256OfTheTokenText()
    {
        var token = AccessLinkToken.New();

        Assert.Equal(SHA256.HashData(Encoding.ASCII.GetBytes(token)), AccessLinkToken.Hash(token));
    }

    [Fact]
    public void TheLifetimes_Are72HoursForTheInvitation_And24ForTheReset()
    {
        Assert.Equal(TimeSpan.FromHours(72), AccessLinkToken.LifetimeOf(AccessLinkPurpose.Invitation));
        Assert.Equal(TimeSpan.FromHours(24), AccessLinkToken.LifetimeOf(AccessLinkPurpose.PasswordReset));
    }
}
