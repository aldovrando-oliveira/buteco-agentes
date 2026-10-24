using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Buteco.Api.Identity;

/// <summary>
/// Monta o principal do grant <c>client_credentials</c> (design.md da change
/// emissor-oidc-api, D2). Quando este handler roda, o OpenIddict já autenticou o cliente
/// e conferiu a permissão de cada escopo pedido; aqui só se decide o que vai no token:
/// <list type="bullet">
/// <item><c>sub</c> = <c>client_id</c>;</item>
/// <item>os escopos concedidos;</item>
/// <item><c>aud</c> = os recursos dos escopos, como a #186 e a #208 vão consumir.</item>
/// </list>
/// Nenhum cliente é cadastrado nesta change, então em produção ele nunca chega a rodar.
/// </summary>
public sealed class ClientCredentialsTokenHandler(IOpenIddictScopeManager scopeManager)
    : IOpenIddictServerHandler<HandleTokenRequestContext>
{
    public async ValueTask HandleAsync(HandleTokenRequestContext context)
    {
        if (!context.Request.IsClientCredentialsGrantType())
        {
            return;
        }

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, context.ClientId);

        var scopes = context.Request.GetScopes();
        identity.SetScopes(scopes);

        var resources = new List<string>();
        await foreach (var resource in scopeManager.ListResourcesAsync(scopes, context.CancellationToken))
        {
            resources.Add(resource);
        }

        identity.SetResources(resources);
        identity.SetDestinations(static _ => [Destinations.AccessToken]);

        context.SignIn(new ClaimsPrincipal(identity));
    }
}
