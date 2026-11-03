namespace Buteco.Api.Identity;

/// <summary>
/// A base do painel (design.md da change convites-redefinicao-senha, D4):
/// <c>Identity:PanelBaseUrl</c> quando configurada (desenvolvimento, com o Vite), senão o issuer,
/// que é <c>PublicUrl:BaseUrl</c>. Dois consumidores: os endereços de retorno do
/// <c>buteco-painel</c> (<c>IdentityClientCatalog</c>) e os links de convite e de redefinição.
/// Nunca vem da requisição (<c>Host</c>, <c>X-Forwarded-Host</c>, esquema): um <c>Host</c> forjado
/// não muda o link.
/// </summary>
public static class PanelBaseUrl
{
    public static Uri Resolve(IdentityIssuerOptions options, IdentityIssuerState issuer) =>
        Uri.TryCreate(options.PanelBaseUrl, UriKind.Absolute, out var panel)
            ? panel
            : issuer.Issuer ?? throw new InvalidOperationException("Emissor sem url pública: o painel não tem base.");
}
