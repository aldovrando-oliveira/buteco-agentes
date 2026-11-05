namespace Buteco.Api.Identity;

/// <summary>
/// As durações dos tokens do emissor, definidas uma vez só (design.md da change
/// leitura-identidade-emissor, D6; decisão do dono de 04/11/2026). Quem lê: o registro do
/// OpenIddict (<see cref="IdentityIssuerServiceCollectionExtensions"/>), o
/// <c>ServiceAccessTokenIssuer</c> e o <c>GET /identity</c>. As da rotação das chaves ficam
/// na <c>IdentityKeyRotationPolicy</c>, que já é a fonte única delas.
///
/// <para>
/// Constantes no código, e não opção (convenção 2). O teste que compara a resposta do
/// <c>GET /identity</c> com as <c>OpenIddictServerOptions</c> do host reprova quando um lado
/// muda sem o outro.
/// </para>
/// </summary>
public static class IdentityTokenLifetimes
{
    /// <summary>
    /// Todo access token, inclusive o de <c>client_credentials</c> (#186) e o que o
    /// <c>apps/api</c> gera para o <c>apps/connectors</c> (design.md da change
    /// usuarios-locais-login-emissor, D3.7).
    /// </summary>
    public static readonly TimeSpan AccessToken = TimeSpan.FromMinutes(15);

    /// <summary>
    /// O refresh token, rotativo e deslizante: cada uso emite outro com a duração inteira, então
    /// a sessão do painel termina depois deste tempo sem atividade. O OpenIddict 7.7.2 só fixa o
    /// vencimento com <c>DisableSlidingRefreshTokenExpiration</c>
    /// (<c>OpenIddictServerHandlers.cs</c> l. 4473–4486), que o Buteco não liga.
    /// </summary>
    public static readonly TimeSpan RefreshToken = TimeSpan.FromDays(14);

    /// <summary>
    /// O id token. É o padrão do OpenIddict 7.7.2 (<c>OpenIddictServerOptions.cs</c> l. 271),
    /// registrado explicitamente para ter uma fonte só.
    /// </summary>
    public static readonly TimeSpan IdentityToken = TimeSpan.FromMinutes(20);

    /// <summary>
    /// O código de autorização. É o padrão do OpenIddict 7.7.2
    /// (<c>OpenIddictServerOptions.cs</c> l. 245), registrado explicitamente para ter uma
    /// fonte só.
    /// </summary>
    public static readonly TimeSpan AuthorizationCode = TimeSpan.FromMinutes(5);
}
