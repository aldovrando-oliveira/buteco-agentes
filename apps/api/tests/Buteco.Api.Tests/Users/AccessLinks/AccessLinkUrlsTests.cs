using Buteco.Api.Identity;
using Buteco.Api.Users.AccessLinks;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// A base e a forma do link (design.md da change convites-redefinicao-senha, D3 e D4).
///
/// <para>
/// Verificado contra o defeito (tasks.md, 3.6): o token no caminho (<c>/convite/&lt;token&gt;</c>)
/// → <see cref="TheToken_GoesOnlyInTheFragment"/> reprova.
/// </para>
/// </summary>
public class AccessLinkUrlsTests
{
    private static readonly IdentityIssuerState Issuer = new(new Uri("https://buteco.exemplo.test/"));

    [Fact]
    public void WithPanelBaseUrl_TheBaseIsThePanels()
    {
        var panelBase = PanelBaseUrl.Resolve(new IdentityIssuerOptions { PanelBaseUrl = "http://localhost:5173" }, Issuer);

        Assert.Equal("http://localhost:5173/convite#tok", AccessLinkUrls.For(AccessLinkPurpose.Invitation, panelBase, "tok"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithoutPanelBaseUrl_TheBaseIsTheIssuer(string? configured)
    {
        var panelBase = PanelBaseUrl.Resolve(new IdentityIssuerOptions { PanelBaseUrl = configured }, Issuer);

        Assert.Equal("https://buteco.exemplo.test/redefinir-senha#tok", AccessLinkUrls.For(AccessLinkPurpose.PasswordReset, panelBase, "tok"));
    }

    [Theory]
    [InlineData("https://buteco.exemplo.test")]
    [InlineData("https://buteco.exemplo.test/")]
    public void WithOrWithoutTrailingSlash_OneSlashOnly(string configured)
    {
        var panelBase = PanelBaseUrl.Resolve(new IdentityIssuerOptions { PanelBaseUrl = configured }, Issuer);

        Assert.Equal("https://buteco.exemplo.test/convite#tok", AccessLinkUrls.For(AccessLinkPurpose.Invitation, panelBase, "tok"));
    }

    [Fact]
    public void TheToken_GoesOnlyInTheFragment()
    {
        var token = AccessLinkToken.New();

        var link = new Uri(AccessLinkUrls.For(AccessLinkPurpose.Invitation, new Uri("https://buteco.exemplo.test"), token));

        Assert.Equal("/convite", link.AbsolutePath);
        Assert.Equal("", link.Query);
        Assert.Equal($"#{token}", link.Fragment);
    }
}
