using Buteco.Api.Auth;

namespace Buteco.Api.Identity;

/// <summary>
/// A lista esperada dos endpoints do emissor, cada um com motivo (design.md da change
/// emissor-oidc-api, D8). É a forma da convenção 8 para endpoint que não passa pelo
/// roteamento: o OpenIddict os atende dentro do <c>UseAuthentication</c> (F5), e a
/// <c>ValidateRouteAuthenticationClassification</c> não os vê.
/// <c>ValidateIdentityIssuerEndpoints</c> compara esta lista, nos dois sentidos, com
/// as URIs que a composição real configura.
///
/// <para>
/// O authorize e o logout são <b>passthrough</b> (design.md da change
/// usuarios-locais-login-emissor, D3.6): depois de o OpenIddict validar o pedido, uma rota
/// mapeada do app decide a resposta. Para eles a checagem exige a rota, anônima e com o
/// mesmo motivo; para os outros, continua proibido coincidir com rota.
/// </para>
/// </summary>
public static class IdentityIssuerEndpoints
{
    public const string Configuration = "/.well-known/openid-configuration";
    public const string JsonWebKeySet = "/.well-known/jwks";
    public const string Token = "/connect/token";
    public const string Authorization = "/connect/authorize";
    public const string EndSession = "/connect/logout";

    public sealed record ExpectedEndpoint(AnonymousRouteReason Reason, bool Passthrough);

    public static IReadOnlyDictionary<string, ExpectedEndpoint> Expected { get; } =
        new Dictionary<string, ExpectedEndpoint>(StringComparer.Ordinal)
        {
            [Configuration] = new(AnonymousRouteReason.PublicDiscovery, Passthrough: false),
            [JsonWebKeySet] = new(AnonymousRouteReason.PublicDiscovery, Passthrough: false),
            [Token] = new(AnonymousRouteReason.AuthEntryPoint, Passthrough: false),
            [Authorization] = new(AnonymousRouteReason.AuthEntryPoint, Passthrough: true),
            [EndSession] = new(AnonymousRouteReason.SessionEnd, Passthrough: true),
        };
}
