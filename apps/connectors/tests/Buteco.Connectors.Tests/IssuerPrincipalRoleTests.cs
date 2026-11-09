using System.Security.Claims;
using Buteco.Connectors.Auth;

namespace Buteco.Connectors.Tests;

/// <summary>
/// "O principal leva o papel do token do painel" (spec <c>issuer-token-validation</c>; design.md da
/// change papeis-no-token, D2 e D3): o <c>role</c> vai só para o principal do <c>buteco-painel</c>.
/// Um role vazando para um cliente de serviço não muda nenhuma resposta, e só o principal o mostra.
/// </summary>
public class IssuerPrincipalRoleTests
{
    private static ClaimsPrincipal Validated(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "jwt"));

    [Fact]
    public void ThePanelRole_IsInThePrincipal_NextToTheSubject()
    {
        var principal = IssuerTokenAuthentication.MapPrincipal(Validated(
            ("client_id", IssuerClientSubjects.PanelClientId), ("sub", "usuario-1"), ("role", "reader")));

        Assert.Equal("reader", principal.FindFirst(PanelRoles.Claim)?.Value);
        Assert.Equal("operator", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public void AServiceClient_GetsNoRole_EvenIfTheTokenHasOne()
    {
        var principal = IssuerTokenAuthentication.MapPrincipal(Validated(
            ("client_id", IssuerClientSubjects.ApiServiceClientId), ("sub", IssuerClientSubjects.ApiServiceClientId), ("role", "admin")));

        Assert.Null(principal.FindFirst(PanelRoles.Claim));
        Assert.Equal("service:api", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }
}
