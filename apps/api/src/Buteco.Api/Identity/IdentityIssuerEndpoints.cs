using Buteco.Api.Auth;

namespace Buteco.Api.Identity;

/// <summary>
/// A lista esperada dos endpoints do emissor, cada um com motivo (design.md da change
/// emissor-oidc-api, D8). É a forma da convenção 8 para endpoint que não passa pelo
/// roteamento: o OpenIddict os atende dentro do <c>UseAuthentication</c> (F5), e a
/// <c>ValidateRouteAuthenticationClassification</c> não os vê.
/// <c>ValidateIdentityIssuerEndpoints</c> compara esta lista, nos dois sentidos, com
/// as URIs que a composição real configura.
/// </summary>
public static class IdentityIssuerEndpoints
{
    public const string Configuration = "/.well-known/openid-configuration";
    public const string JsonWebKeySet = "/.well-known/jwks";
    public const string Token = "/connect/token";

    public static IReadOnlyDictionary<string, AnonymousRouteReason> Expected { get; } =
        new Dictionary<string, AnonymousRouteReason>(StringComparer.Ordinal)
        {
            [Configuration] = AnonymousRouteReason.PublicDiscovery,
            [JsonWebKeySet] = AnonymousRouteReason.PublicDiscovery,
            [Token] = AnonymousRouteReason.AuthEntryPoint,
        };
}
