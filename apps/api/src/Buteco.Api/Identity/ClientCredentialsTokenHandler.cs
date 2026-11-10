using System.Security.Claims;
using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
/// <item><c>aud</c> = os recursos dos escopos. Sem <c>scope</c> no pedido, o token sai sem <c>aud</c>,
/// e o <c>apps/api</c> o recusa com <c>401</c> (design.md da change aplicacoes-externas-credencial,
/// Q4);</item>
/// <item><b>para a aplicação externa, e só para ela</b> (aplicacoes-externas-credencial, D1 e D5): o
/// claim <see cref="ApplicationClaim"/> com o id da aplicação, que é o que o <c>apps/api</c> lê para
/// dar o subject <c>application</c>, e a duração de
/// <see cref="IdentityTokenLifetimes.ApplicationAccessToken"/> posta no principal. Quem decide que o
/// cliente é aplicação é o cadastro, não o <c>client_id</c>: o <c>buteco-painel</c> e os
/// <c>buteco-servico-*</c> nunca levam o claim.</item>
/// </list>
/// Quem chega aqui em produção: os dois clientes de serviço (#182) e as aplicações externas (#186).
/// </summary>
public sealed class ClientCredentialsTokenHandler(IOpenIddictScopeManager scopeManager, AppDbContext dbContext)
    : IOpenIddictServerHandler<HandleTokenRequestContext>
{
    /// <summary>O id da aplicação externa no access token (GUID).</summary>
    public const string ApplicationClaim = "buteco_app";

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

        var applicationId = await dbContext.ExternalApplications.AsNoTracking()
            .Where(application => application.ClientId == context.ClientId)
            .Select(application => (Guid?)application.Id)
            .SingleOrDefaultAsync(context.CancellationToken);
        if (applicationId is { } id)
        {
            identity.SetClaim(ApplicationClaim, id.ToString());
            identity.SetAccessTokenLifetime(IdentityTokenLifetimes.ApplicationAccessToken);
        }

        identity.SetDestinations(static _ => [Destinations.AccessToken]);

        context.SignIn(new ClaimsPrincipal(identity));
    }
}
