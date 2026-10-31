using Buteco.Api.Auth;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Buteco.Api.Identity.Flow;

/// <summary>
/// <c>GET /connect/logout</c> em passthrough (design.md da change
/// usuarios-locais-login-emissor, D3.9). Chega aqui todo pedido com
/// <c>post_logout_redirect_uri</c> ausente ou registrado, <b>inclusive com hint inválido</b>:
/// no logout o hint é opcional, e o OpenIddict o valida sem recusar o pedido
/// (<c>OpenIddictServerHandlers.cs</c> l. 364–377 e 2094–2105). O endereço não registrado é
/// recusado antes, e é do <see cref="EndSessionErrorHandler"/>.
/// <list type="bullet">
/// <item>revoga a autorização do <c>oi_au_id</c> do hint. O OpenIddict não confere essa
/// autorização ao validar o hint (2.4 (b)), então um hint de sessão já revogada chega aqui,
/// e revogar de novo não faz nada;</item>
/// <item>apaga o cookie <c>buteco-sessao</c>;</item>
/// <item>só com hint válido e endereço pedido redireciona ao <c>post_logout_redirect_uri</c>;
/// sem hint válido, ou sem endereço, vai a <c>/entrar</c>.</item>
/// </list>
/// </summary>
public static class EndSessionEndpoint
{
    public static IEndpointRouteBuilder MapEndSessionEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet(IdentityIssuerEndpoints.EndSession, LogoutAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.SessionEnd));

        return app;
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        IOpenIddictAuthorizationManager authorizations,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Pedido de encerramento sem o contexto do OpenIddict.");

        // Sem hint válido, o OpenIddict devolve Success com um principal VAZIO, e não nulo
        // (OpenIddictServerAspNetCoreHandler.cs l. 207–209): válido é o autenticado.
        var principal = (await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        var hint = principal?.Identity?.IsAuthenticated == true ? principal : null;
        if (hint?.GetAuthorizationId() is { } authorizationId &&
            await authorizations.FindByIdAsync(authorizationId, cancellationToken) is { } authorization)
        {
            await authorizations.TryRevokeAsync(authorization, cancellationToken);
        }

        await httpContext.SignOutAsync(IssuerSession.SchemeName);

        // Sem hint válido, ou sem endereço de volta (o OpenIddict responderia 200 vazio), o
        // painel volta ao login.
        if (hint is null || string.IsNullOrEmpty(request.PostLogoutRedirectUri))
        {
            return Results.Redirect(AuthorizeEndpoint.LoginPath);
        }

        return Results.SignOut(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }
}
