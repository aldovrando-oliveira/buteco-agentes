using System.Security.Claims;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.Validation;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O principal do token do emissor no <c>apps/api</c> (spec <c>user-account</c>, "Lista das
/// sessões vivas, com a sessão atual marcada"; design.md da change rotas-conta-usuarios, D4):
/// o <c>oi_au_id</c> vai como está, ao lado do <c>sub</c>, e o subject continua só pela tabela
/// de clientes.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.2: com a cópia do
/// <c>oi_au_id</c> retirada, <see cref="KeepsTheAuthorizationId_NextToTheSub"/> reprova.
/// </para>
/// <para>
/// Os clientes de serviço (design.md da change clientes-servico-emissor, D3), verificados
/// contra o defeito (registro no tasks.md daquela change, 4.2): com a entrada do inbox
/// retirada da tabela, <see cref="AServiceClient_MapsToItsServiceSubject"/> reprova.
/// </para>
/// </summary>
public class IssuerPrincipalMappingTests
{
    private static ClaimsPrincipal Validated(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "jwt"));

    [Fact]
    public void KeepsTheAuthorizationId_NextToTheSub()
    {
        var principal = IssuerTokenAuthentication.MapPrincipal(Validated(
            ("client_id", IdentityClients.PanelClientId), ("sub", "usuario-1"), ("oi_au_id", "autorizacao-1"), ("oi_prst", IdentityClients.PanelClientId)));

        Assert.Equal("autorizacao-1", principal.FindFirst(IssuerTokenAuthentication.AuthorizationIdClaim)?.Value);
        Assert.Equal("usuario-1", principal.FindFirst(IssuerTokenAuthentication.SubjectClaim)?.Value);
        Assert.Null(principal.FindFirst("oi_prst"));
    }

    [Fact]
    public void TheSubjectStillComesFromTheClientTable()
    {
        var principal = IssuerTokenAuthentication.MapPrincipal(Validated(
            ("client_id", IdentityClients.PanelClientId), ("sub", "operator-forjado"), ("oi_au_id", "autorizacao-1")));

        Assert.Equal(ServiceScopeAuthorizationHandler.OperatorSubject, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Theory]
    [InlineData(IdentityClients.InboxServiceClientId, ServiceScopeAuthorizationHandler.InboxSubject)]
    [InlineData(IdentityClients.ConnectorsServiceClientId, ServiceScopeAuthorizationHandler.ConnectorsSubject)]
    public void AServiceClient_MapsToItsServiceSubject(string clientId, string subject)
    {
        // No client_credentials, o sub é o próprio client_id; quem decide é a tabela.
        var principal = IssuerTokenAuthentication.MapPrincipal(Validated(("client_id", clientId), ("sub", clientId)));

        Assert.Equal(subject, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public void ClientOutsideTheTable_HasNoNameIdentifier()
    {
        var principal = IssuerTokenAuthentication.MapPrincipal(Validated(
            ("client_id", "desconhecido"), ("sub", "usuario-1"), ("oi_au_id", "autorizacao-1")));

        Assert.Null(principal.FindFirst(ClaimTypes.NameIdentifier));
    }
}
