using Buteco.Api.Identity.Clients;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O descritor do <c>buteco-painel</c> e dos escopos (spec <c>issuer-user-login</c>, "Cliente
/// público buteco-painel e três escopos mantidos pelo apps/api"; design.md da change
/// usuarios-locais-login-emissor, D3.1 e D3.2).
///
/// <para>
/// Verificado contra o defeito (convenção 15): com
/// <c>Permissions.GrantTypes.ClientCredentials</c> no descritor,
/// <see cref="Panel_IsPublic_WithPkce_WithoutSecretOrClientCredentials"/> reprova (registro no
/// tasks.md, 6.3).
/// </para>
/// </summary>
public class IdentityClientCatalogTests
{
    private static readonly Uri PanelBase = new("https://painel.exemplo.test");

    [Fact]
    public void Panel_IsPublic_WithPkce_WithoutSecretOrClientCredentials()
    {
        var panel = IdentityClients.Panel(PanelBase);

        Assert.Equal("buteco-painel", panel.ClientId);
        Assert.Equal(ClientTypes.Public, panel.ClientType);
        Assert.Null(panel.ClientSecret);
        Assert.Equal(ConsentTypes.Implicit, panel.ConsentType);
        Assert.Contains(Requirements.Features.ProofKeyForCodeExchange, panel.Requirements);
        Assert.DoesNotContain(Permissions.GrantTypes.ClientCredentials, panel.Permissions);
        Assert.Contains(Permissions.GrantTypes.AuthorizationCode, panel.Permissions);
        Assert.Contains(Permissions.GrantTypes.RefreshToken, panel.Permissions);
        Assert.Contains(Permissions.Endpoints.EndSession, panel.Permissions);
    }

    [Fact]
    public void Panel_ReturnAddresses_AreUnderThePanelBase()
    {
        var panel = IdentityClients.Panel(PanelBase);

        Assert.Equal([new Uri("https://painel.exemplo.test/entrar/retorno")], panel.RedirectUris);
        Assert.Equal([new Uri("https://painel.exemplo.test/entrar")], panel.PostLogoutRedirectUris);
    }

    [Fact]
    public void Scopes_AreTheThreeApps_EachWithTheResourceOfTheSameName()
    {
        Assert.Equal(["buteco-api", "buteco-inbox", "buteco-connectors"], IdentityClients.Scopes);
        foreach (var name in IdentityClients.Scopes)
        {
            Assert.Equal([name], IdentityClients.Scope(name).Resources);
        }
    }

    [Fact]
    public void AdvisoryLockKey_IsBUTECOC1_ReadAsABigEndianInteger()
    {
        var bytes = BitConverter.GetBytes(IdentityClientCatalog.AdvisoryLockKey);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        Assert.Equal("BUTECOC1", System.Text.Encoding.ASCII.GetString(bytes));
    }
}
