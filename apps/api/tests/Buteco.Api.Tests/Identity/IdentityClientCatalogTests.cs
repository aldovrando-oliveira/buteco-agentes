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

    // Os dois clientes de serviço (design.md da change clientes-servico-emissor, D1). Verificado
    // contra o defeito (convenção 15): com scp:buteco-connectors a mais no descritor, o
    // conjunto de permissões não confere e o teste reprova (registro no tasks.md, 1.3).
    [Theory]
    [InlineData(IdentityClients.InboxServiceClientId)]
    [InlineData(IdentityClients.ConnectorsServiceClientId)]
    public void ServiceClient_IsConfidential_WithOnlyTokenClientCredentialsAndTheApiScope(string clientId)
    {
        var service = IdentityClients.Service(clientId, "Serviço", "segredo-de-teste");

        Assert.Equal(clientId, service.ClientId);
        Assert.Equal(ClientTypes.Confidential, service.ClientType);
        Assert.Equal("segredo-de-teste", service.ClientSecret);
        Assert.Equal(ConsentTypes.Implicit, service.ConsentType);
        Assert.Equal(
            new HashSet<string>
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + IdentityClients.ApiScope,
            },
            service.Permissions);
        Assert.Empty(service.RedirectUris);
        Assert.Empty(service.PostLogoutRedirectUris);
        Assert.Empty(service.Requirements);
    }

    [Fact]
    public void ServiceClientIds_UseTheReservedPrefix_AndAreNotScopeNames()
    {
        foreach (var clientId in new[] { IdentityClients.InboxServiceClientId, IdentityClients.ConnectorsServiceClientId })
        {
            Assert.StartsWith(IdentityClients.ReservedPrefix, clientId, StringComparison.Ordinal);
            Assert.DoesNotContain(clientId, IdentityClients.Scopes);
        }
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
