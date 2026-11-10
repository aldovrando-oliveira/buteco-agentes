using System.Security.Claims;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Validation;

namespace Buteco.Api.A2A;

/// <summary>
/// Quem chama a rota A2A, lido do principal (design.md da change agentes-por-aplicacao, D2 e D3). O claim
/// <c>buteco_app</c> é posto pelo emissor só para quem está no cadastro e preservado pelo
/// <c>IssuerTokenAuthentication.MapPrincipal</c> só quando o subject é <c>application</c>. Qualquer outro
/// chamador (o <c>service:inbox</c>) não é aplicação e segue o caminho de antes desta change, sem filtro.
///
/// <para>
/// <b>O subject decide, não o claim.</b> Um subject <c>application</c> com o claim ausente ou ilegível é
/// aplicação <i>sem</i> id, e não um chamador sem filtro: o filtro o recusa com o <c>404</c>, como uma
/// aplicação sem vínculo. Ler "não tem id, então não é aplicação" abriria a rota inteira.
/// </para>
/// </summary>
public static class ApplicationCaller
{
    public static bool IsApplication(ClaimsPrincipal? user) =>
        user?.FindFirst(ClaimTypes.NameIdentifier)?.Value == ServiceScopeAuthorizationHandler.ApplicationSubject;

    /// <summary>O id da aplicação, ou <c>null</c> se quem chama não é aplicação ou não traz id legível.</summary>
    public static Guid? IdOf(ClaimsPrincipal? user) =>
        IsApplication(user) && Guid.TryParse(user!.FindFirst(IssuerTokenAuthentication.ApplicationClaim)?.Value, out var id)
            ? id
            : null;
}
