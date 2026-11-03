using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Identity.Clients;

/// <summary>
/// Os clientes e os três escopos que o próprio <c>apps/api</c> mantém: o <c>buteco-painel</c>
/// (design.md da change usuarios-locais-login-emissor, D3.1 e D3.2) e os dois clientes de
/// serviço (design.md da change clientes-servico-emissor, D1). Fixos no código: nenhum outro
/// cliente, escopo ou recurso é cadastrado por migration nem por código. O
/// <see cref="IdentityClientCatalog"/> sincroniza e corrige.
/// </summary>
public static class IdentityClients
{
    public const string PanelClientId = "buteco-painel";

    /// <summary>
    /// Os clientes de serviço, um por serviço que chama o <c>apps/api</c>: o principal deles é
    /// <c>service:inbox</c> e <c>service:connectors</c>, pela tabela de clientes. O infixo
    /// <c>servico</c> separa o cliente do escopo de mesmo recurso.
    /// </summary>
    public const string InboxServiceClientId = "buteco-servico-inbox";
    public const string ConnectorsServiceClientId = "buteco-servico-connectors";

    /// <summary>Prefixo reservado: um cliente com ele que o código não declara gera log de erro.</summary>
    public const string ReservedPrefix = "buteco-";

    public const string ApiScope = "buteco-api";
    public const string InboxScope = "buteco-inbox";
    public const string ConnectorsScope = "buteco-connectors";

    /// <summary>Cada escopo com o recurso de mesmo nome, que vira a <c>aud</c> do token.</summary>
    public static IReadOnlyList<string> Scopes { get; } = [ApiScope, InboxScope, ConnectorsScope];

    /// <summary>Caminhos de retorno no painel, sob a base dele.</summary>
    public const string RedirectPath = "/entrar/retorno";
    public const string PostLogoutRedirectPath = "/entrar";

    /// <summary>
    /// O <c>buteco-painel</c>: público, sem segredo, consentimento implícito, PKCE exigido,
    /// <c>authorization_code</c>, <c>refresh_token</c> e encerramento de sessão, e sem
    /// <c>client_credentials</c>. A base do painel é <c>Identity:PanelBaseUrl</c> ou, sem
    /// ela, a url pública.
    /// </summary>
    public static OpenIddictApplicationDescriptor Panel(Uri panelBase)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = PanelClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Painel do Buteco",
            RedirectUris = { new Uri(panelBase, RedirectPath) },
            PostLogoutRedirectUris = { new Uri(panelBase, PostLogoutRedirectPath) },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var permission in new[]
                 {
                     Permissions.Endpoints.Authorization,
                     Permissions.Endpoints.Token,
                     Permissions.Endpoints.EndSession,
                     Permissions.GrantTypes.AuthorizationCode,
                     Permissions.GrantTypes.RefreshToken,
                     Permissions.ResponseTypes.Code,
                 })
        {
            descriptor.Permissions.Add(permission);
        }

        foreach (var scope in Scopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        return descriptor;
    }

    /// <summary>
    /// Um cliente de serviço: confidencial, com o segredo da configuração, consentimento
    /// implícito, e só o endpoint de token, o <c>client_credentials</c> e o escopo
    /// <c>buteco-api</c>. Sem URI e sem requisito. O token sai com <c>aud</c> <c>buteco-api</c>
    /// e nada mais: a credencial de um serviço não alcança o <c>apps/inbox</c> nem o
    /// <c>apps/connectors</c>.
    /// </summary>
    public static OpenIddictApplicationDescriptor Service(string clientId, string displayName, string secret)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = displayName,
        };

        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        descriptor.Permissions.Add(Permissions.Prefixes.Scope + ApiScope);

        return descriptor;
    }

    public static OpenIddictScopeDescriptor Scope(string name) => new()
    {
        Name = name,
        Resources = { name },
    };
}
