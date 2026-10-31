using Buteco.Api.Auth;
using Buteco.Api.Infrastructure;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Identity.Flow;

/// <summary>
/// <c>GET /connect/authorize</c> em passthrough (design.md da change
/// usuarios-locais-login-emissor, D1). Quando chega aqui, o OpenIddict já validou o pedido:
/// cliente, <c>redirect_uri</c>, PKCE e escopos (2.4 (a)). Esta rota só decide pela sessão
/// do emissor:
/// <list type="bullet">
/// <item><b>sem sessão válida e sem <c>prompt=none</c>:</b> <c>302</c> para
/// <c>/entrar?retorno=…</c>. O <c>retorno</c> é montado aqui, só com o caminho e a query do
/// pedido, URL-codificado: nunca leva esquema, host nem porta, e nunca sai de parâmetro do
/// cliente nem do header <c>Host</c>;</item>
/// <item><b>sem sessão válida e com <c>prompt=none</c>:</b> <c>login_required</c> ao
/// <c>redirect_uri</c>;</item>
/// <item><b>com cookie de usuário ativo e stamp igual ao gravado:</b> <c>SignIn</c>, e o
/// OpenIddict responde com o código.</item>
/// </list>
/// </summary>
public static class AuthorizeEndpoint
{
    public const string LoginPath = "/entrar";

    public static IEndpointRouteBuilder MapAuthorizeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet(IdentityIssuerEndpoints.Authorization, AuthorizeAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.AuthEntryPoint));

        return app;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext httpContext,
        AppDbContext dbContext,
        IOpenIddictScopeManager scopeManager,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Pedido de autorização sem o contexto do OpenIddict.");

        var user = await SessionUserAsync(httpContext, dbContext, cancellationToken);
        if (user is null)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Results.Forbid(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Sem sessão no emissor.",
                    }),
                    [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            var retorno = $"{httpContext.Request.PathBase}{httpContext.Request.Path}{httpContext.Request.QueryString}";
            return Results.Redirect($"{LoginPath}?retorno={Uri.EscapeDataString(retorno)}");
        }

        var scopes = request.GetScopes();
        var resources = new List<string>();
        await foreach (var resource in scopeManager.ListResourcesAsync(scopes, cancellationToken))
        {
            resources.Add(resource);
        }

        return Results.SignIn(
            UserPrincipalFactory.Create(user, scopes, resources),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>O usuário do cookie, se ativo e com o stamp do cookie igual ao gravado.</summary>
    private static async Task<Users.User?> SessionUserAsync(HttpContext httpContext, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var session = await httpContext.AuthenticateAsync(IssuerSession.SchemeName);
        if (!session.Succeeded ||
            !Guid.TryParse(session.Principal.FindFirst(IssuerSession.UserIdClaim)?.Value, out var userId))
        {
            return null;
        }

        var stamp = session.Principal.FindFirst(IssuerSession.SecurityStampClaim)?.Value;
        var user = await dbContext.Users.FindAsync([userId], cancellationToken);
        return user is { IsActive: true } && user.SecurityStamp == stamp ? user : null;
    }
}
