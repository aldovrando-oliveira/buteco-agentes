namespace Buteco.Api.Identity;

/// <summary>
/// Configuração do emissor OIDC (design.md da change emissor-oidc-api, D4 e D8). O
/// issuer não está aqui: vem de <c>PublicUrl:BaseUrl</c>, o mesmo ponto dos
/// endereços A2A, e não existe segunda configuração de url.
/// </summary>
public sealed class IdentityIssuerOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// 32 bytes em base64 que cifram o material privado de <c>identity_keys</c>.
    /// Vem de configuração ou ambiente (<c>Identity__SigningKeyEncryptionKey</c>),
    /// nunca do banco, e com valor distinto de <c>Mcp:CredentialEncryptionKey</c>.
    /// </summary>
    public string? SigningKeyEncryptionKey { get; set; }
}
