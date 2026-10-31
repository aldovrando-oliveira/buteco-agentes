using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Cadastra o escopo e o cliente confidencial que os testes do emissor usam (design.md
/// da change emissor-oidc-api, D2). <b>Só aqui, depois do <c>MigrateAsync</c></b>: em
/// produção não há cliente nenhum, e o banco recém-migrado não tem linha de aplicação
/// (<c>IdentityIssuerMigrationTests</c>). O primeiro cliente real é da #186.
/// </summary>
public static class IdentityIssuerTestClient
{
    public const string ClientId = "cliente-teste";
    public const string ClientSecret = "segredo-do-cliente-teste";
    public const string Scope = "teste.api";
    public const string Resource = "buteco-teste";

    public static async Task EnsureAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        if (await scopes.FindByNameAsync(Scope) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = Scope,
                Resources = { Resource },
            });
        }

        if (await applications.FindByClientIdAsync(ClientId) is null)
        {
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                ClientType = ClientTypes.Confidential,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + Scope,
                },
            });
        }
    }

    /// <summary>
    /// Variante com refresh, só para a fixture da limpeza (design.md da change
    /// limpeza-tokens-openiddict, D7): com <c>gt:refresh_token</c>, o
    /// <c>client_credentials</c> com <c>offline_access</c> faz o OpenIddict gravar o que o
    /// login da #180 vai gravar, autorização ad hoc e refresh token ligados. Um
    /// <paramref name="clientId"/> por sessão dá um <c>subject</c> por sessão. Precisa do
    /// servidor com <c>AllowRefreshTokenFlow()</c>, que só o teste liga.
    /// </summary>
    public static async Task EnsureRefreshClientAsync(IServiceProvider services, string clientId)
    {
        await EnsureAsync(services);

        await using var scope = services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        if (await applications.FindByClientIdAsync(clientId) is null)
        {
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = ClientSecret,
                ClientType = ClientTypes.Confidential,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.Prefixes.Scope + Scope,
                },
            });
        }
    }

    public static FormUrlEncodedContent RefreshSessionRequest(string clientId) =>
        TokenRequest(clientId: clientId, scope: $"{Scope} {Scopes.OfflineAccess}");

    public static FormUrlEncodedContent RefreshRequest(string clientId, string refreshToken) =>
        new(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.RefreshToken,
            [Parameters.ClientId] = clientId,
            [Parameters.ClientSecret] = ClientSecret,
            [Parameters.RefreshToken] = refreshToken,
        });

    public static FormUrlEncodedContent TokenRequest(
        string grantType = GrantTypes.ClientCredentials,
        string clientId = ClientId,
        string clientSecret = ClientSecret,
        string? scope = Scope) =>
        new(new Dictionary<string, string>
        {
            [Parameters.GrantType] = grantType,
            [Parameters.ClientId] = clientId,
            [Parameters.ClientSecret] = clientSecret,
        }.Concat(scope is null ? [] : [KeyValuePair.Create(Parameters.Scope, scope)]));
}

/// <summary>
/// O fluxo do usuário pelo <c>TestServer</c>, sem navegador (design.md da change
/// usuarios-locais-login-emissor, D5): sessão, authorize com PKCE e troca do código. Os
/// pedidos vão sem cookie automático nem redirecionamento seguido, para o teste ver cada
/// resposta.
/// </summary>
public static class IdentityIssuerUserFlow
{
    public const string AllScopes = "openid offline_access buteco-api buteco-inbox buteco-connectors";

    public static readonly string RedirectUri = new Uri(new Uri(ApiFactoryFixture.PublicBaseUrl), "/entrar/retorno").AbsoluteUri;
    public static readonly string PostLogoutRedirectUri = new Uri(new Uri(ApiFactoryFixture.PublicBaseUrl), "/entrar").AbsoluteUri;

    public sealed record Tokens(string AccessToken, string RefreshToken, string IdToken);

    public sealed record Pkce(string Verifier, string Challenge)
    {
        public static Pkce Create()
        {
            var verifier = Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
            return new Pkce(verifier, challenge);
        }
    }

    public static HttpClient Client(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });

    /// <summary>O catálogo roda no aquecimento; aqui roda já, para o teste não depender do ciclo.</summary>
    public static async Task EnsurePanelClientAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<Buteco.Api.Identity.Clients.IIdentityClientCatalog>().SyncAsync(CancellationToken.None);
    }

    /// <summary>O valor <c>buteco-sessao=…</c> do login, pronto para o header <c>Cookie</c>.</summary>
    public static async Task<string> SessionCookieAsync(HttpClient client, string email, string password)
    {
        var response = await LocalUserSeed.PostSessionAsync(client, email, password);
        if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"Login falhou: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    public static string AuthorizeUrl(Pkce? pkce, string scope = AllScopes, string? prompt = null, string redirectUri = "", string clientId = "buteco-painel")
    {
        var query = new Dictionary<string, string?>
        {
            [Parameters.ClientId] = clientId,
            [Parameters.ResponseType] = ResponseTypes.Code,
            [Parameters.Scope] = scope,
            [Parameters.RedirectUri] = string.IsNullOrEmpty(redirectUri) ? RedirectUri : redirectUri,
            [Parameters.State] = "estado-de-teste",
            [Parameters.CodeChallenge] = pkce?.Challenge,
            [Parameters.CodeChallengeMethod] = pkce is null ? null : CodeChallengeMethods.Sha256,
            [Parameters.Prompt] = prompt,
        };

        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/connect/authorize",
            query.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    public static Task<HttpResponseMessage> AuthorizeAsync(HttpClient client, string url, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return client.SendAsync(request);
    }

    /// <summary>O código do <c>Location</c> de volta ao <c>redirect_uri</c>.</summary>
    public static string CodeFrom(HttpResponseMessage authorize)
    {
        var location = authorize.Headers.Location ?? throw new InvalidOperationException($"Authorize sem Location: {(int)authorize.StatusCode}");
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query);
        return query.TryGetValue(Parameters.Code, out var code)
            ? code.ToString()
            : throw new InvalidOperationException($"Authorize sem código: {location}");
    }

    public static Task<HttpResponseMessage> RedeemAsync(HttpClient client, string code, string verifier) =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.AuthorizationCode,
            [Parameters.ClientId] = "buteco-painel",
            [Parameters.Code] = code,
            [Parameters.RedirectUri] = RedirectUri,
            [Parameters.CodeVerifier] = verifier,
        }));

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.RefreshToken,
            [Parameters.ClientId] = "buteco-painel",
            [Parameters.RefreshToken] = refreshToken,
        }));

    public static async Task<Tokens> ReadTokensAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Token falhou: {(int)response.StatusCode} {body}");
        }

        var root = System.Text.Json.JsonDocument.Parse(body).RootElement;
        return new Tokens(
            root.GetProperty(Parameters.AccessToken).GetString()!,
            root.GetProperty(Parameters.RefreshToken).GetString()!,
            root.GetProperty(Parameters.IdToken).GetString()!);
    }

    /// <summary>Sessão, authorize e troca, de uma vez: o par de tokens do usuário.</summary>
    public static async Task<Tokens> SignInAsync(HttpClient client, IServiceProvider services, string email, string password)
    {
        await EnsurePanelClientAsync(services);
        var cookie = await SessionCookieAsync(client, email, password);
        var pkce = Pkce.Create();
        var authorize = await AuthorizeAsync(client, AuthorizeUrl(pkce), cookie);
        return await ReadTokensAsync(await RedeemAsync(client, CodeFrom(authorize), pkce.Verifier));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
