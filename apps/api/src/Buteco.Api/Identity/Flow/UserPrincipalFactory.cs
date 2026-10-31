using System.Security.Claims;
using Buteco.Api.Users;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Identity.Flow;

/// <summary>
/// O principal do usuário no authorize (design.md da change usuarios-locais-login-emissor,
/// D1 e D4):
/// <list type="bullet">
/// <item><c>sub</c> = id do usuário, no access token e no id token;</item>
/// <item>e-mail e nome só no id token, que o painel lê;</item>
/// <item>os escopos pedidos e, como <c>aud</c>, os recursos deles;</item>
/// <item>o <c>security_stamp</c> <b>sem destino</b>: o OpenIddict guarda no código e no
/// refresh todos os claims (<c>OpenIddictServerHandlers.cs</c> l. 3809 e 4467), e os
/// destinos só filtram o access e o id token (l. 3662 e 4648). O stamp não sai em nenhum
/// token que o cliente lê, e todo token novo de usuário o confere
/// (<see cref="UserGrantTokenHandler"/>).</item>
/// </list>
/// </summary>
public static class UserPrincipalFactory
{
    public static ClaimsPrincipal Create(User user, IEnumerable<string> scopes, IEnumerable<string> resources)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.Name)
            .SetClaim(IssuerSession.SecurityStampClaim, user.SecurityStamp);

        identity.SetScopes(scopes);
        identity.SetResources(resources);
        identity.SetDestinations(static claim => claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Email or Claims.Name => [Destinations.IdentityToken],
            IssuerSession.SecurityStampClaim => [],
            _ => [Destinations.AccessToken],
        });

        return new ClaimsPrincipal(identity);
    }
}
