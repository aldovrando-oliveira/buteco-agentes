namespace Buteco.Api.Identity.Keys;

/// <summary>
/// Uso de uma chave do emissor (design.md da change emissor-oidc-api, D3). Gravado
/// como texto em <c>identity_keys.usage</c>.
/// </summary>
public enum IdentityKeyUsage
{
    /// <summary>RSA 2048, RS256: assina os tokens e é publicada no JWKS.</summary>
    Signing,

    /// <summary>
    /// AES-256: a credencial de cifra que o OpenIddict exige (<c>ID0085</c>). Nada é
    /// cifrado com ela nesta change; a #180 cifra refresh tokens com ela.
    /// </summary>
    Encryption,
}
