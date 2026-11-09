using System.Security.Claims;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.Validation;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// "O principal leva o papel do token do painel" (spec <c>issuer-token-validation</c>; design.md da
/// change papeis-no-token, D2 e D3): o <c>role</c> vai só para o principal do <c>buteco-painel</c>.
/// </summary>
public class IssuerPrincipalRoleTests
{
    private static ClaimsPrincipal Validated(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "jwt"));

    [Fact]
    public void ThePanelRole_IsInThePrincipal_NextToTheSubject()
    {
        var principal = IssuerTokenAuthentication.MapPrincipal(Validated(
            ("client_id", IdentityClients.PanelClientId), ("sub", "usuario-1"), ("role", "reader")));

        Assert.Equal("reader", principal.FindFirst(IssuerTokenAuthentication.RoleClaim)?.Value);
        Assert.Equal("operator", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public void AServiceClient_GetsNoRole_EvenIfTheTokenHasOne()
    {
        var principal = IssuerTokenAuthentication.MapPrincipal(Validated(
            ("client_id", IdentityClients.ConnectorsServiceClientId), ("sub", IdentityClients.ConnectorsServiceClientId), ("role", "admin")));

        Assert.Null(principal.FindFirst(IssuerTokenAuthentication.RoleClaim));
        Assert.Equal("service:connectors", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }
}
