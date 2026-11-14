using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// O duplo do Google (design.md da change provedores-federados-google, D11): responde por
/// <c>https://accounts.google.com/</c> ao discovery, ao JWKS, ao token e ao userinfo, sem rede. Entra no
/// <c>HttpClient</c> do <c>OpenIddict.Client.SystemNetHttp</c> como um <see cref="DelegatingHandler"/> na
/// frente do handler primário (que o OpenIddict exige ser um <c>HttpClientHandler</c>) e nunca passa o
/// pedido adiante.
///
/// <para>
/// <b>O token confere o PKCE</b> (o <c>code_verifier</c> contra o <c>code_challenge</c> do pedido) e o código
/// vale uma vez: sem isso, os guardas do PKCE passariam contra um duplo que aceita tudo (tasks.md, 4.3). O id
/// token sai do <see cref="Account"/> registrado para o código, com cada desvio que os casos precisam.
/// </para>
/// </summary>
public sealed class FakeGoogle
{
    public const string Issuer = "https://accounts.google.com";
    public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string UserinfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
    public const string JwksUri = "https://www.googleapis.com/oauth2/v3/certs";

    public const string ClientId = "1234567890-abc.apps.googleusercontent.com";
    public const string ClientSecret = "GOCSPX-segredo-do-teste-federado";

    /// <summary>A conta que o Google "confirma", e os desvios do id token.</summary>
    public sealed record Account(string Subject, string? Email, bool EmailVerified = true)
    {
        public string? HostedDomain { get; init; }

        public string? IssuerOverride { get; init; }

        public string? AudienceOverride { get; init; }

        public string? NonceOverride { get; init; }

        public bool SignWithAnotherKey { get; init; }

        public TimeSpan ExpiresIn { get; init; } = TimeSpan.FromHours(1);
    }

    private sealed record Grant(string Challenge, string Nonce, Account Account);

    private readonly ConcurrentDictionary<string, Grant> _grants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Account> _accessTokens = new(StringComparer.Ordinal);

    public RsaSecurityKey SigningKey { get; } = new(RSA.Create(2048)) { KeyId = "fake-google-1" };

    private RsaSecurityKey OtherKey { get; } = new(RSA.Create(2048)) { KeyId = "fake-google-1" };

    public enum Availability
    {
        Up,

        /// <summary>Recusa a conexão, como um provedor fora do ar ou sem rede.</summary>
        Down,

        /// <summary>Não responde até o pedido ser cancelado, como uma rede que engole os pacotes.</summary>
        Hanging,
    }

    /// <summary>O estado do provedor para os próximos pedidos (D15, APPLY-11).</summary>
    public Availability State { get; set; } = Availability.Up;

    /// <summary>Quantos pedidos de discovery chegaram (o cache do cliente OIDC, D15 APPLY-11).</summary>
    public int DiscoveryRequests;

    /// <summary>Quantos pedidos de token chegaram (para afirmar que uma recusa veio antes da troca).</summary>
    public int TokenRequests;

    /// <summary>Registra o consentimento: o código que o retorno vai levar, amarrado ao PKCE e ao nonce do pedido.</summary>
    public string Consent(string codeChallenge, string nonce, Account account)
    {
        var code = $"code-{Guid.NewGuid():N}";
        _grants[code] = new Grant(codeChallenge, nonce, account);
        return code;
    }

    public DelegatingHandler CreateHandler() => new Handler(this);

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        switch (State)
        {
            case Availability.Down:
                throw new HttpRequestException("duplo do Google: conexão recusada", new System.Net.Sockets.SocketException(61));
            case Availability.Hanging:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                break;
        }

        var uri = request.RequestUri!.GetLeftPart(UriPartial.Path);
        if (uri == Issuer + "/.well-known/openid-configuration")
        {
            Interlocked.Increment(ref DiscoveryRequests);
        }

        return uri switch
        {
            Issuer + "/.well-known/openid-configuration" => Json(Discovery()),
            JwksUri => Json(Jwks()),
            TokenEndpoint when request.Method == HttpMethod.Post => await TokenAsync(request, cancellationToken),
            UserinfoEndpoint => Userinfo(request),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"duplo do Google: {request.Method} {uri} não existe") },
        };
    }

    private static object Discovery() => new Dictionary<string, object>
    {
        ["issuer"] = Issuer,
        ["authorization_endpoint"] = AuthorizationEndpoint,
        ["token_endpoint"] = TokenEndpoint,
        ["userinfo_endpoint"] = UserinfoEndpoint,
        ["jwks_uri"] = JwksUri,
        ["response_types_supported"] = new[] { "code", "token", "id_token", "code token", "code id_token", "token id_token", "code token id_token", "none" },
        ["subject_types_supported"] = new[] { "public" },
        ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
        ["scopes_supported"] = new[] { "openid", "email", "profile" },
        ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_post", "client_secret_basic" },
        ["claims_supported"] = new[] { "aud", "email", "email_verified", "exp", "iat", "iss", "name", "sub" },
        ["code_challenge_methods_supported"] = new[] { "plain", "S256" },
        ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
    };

    private object Jwks()
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(SigningKey.Rsa.ExportParameters(false)) { KeyId = SigningKey.KeyId });
        return new { keys = new[] { new { kty = jwk.Kty, kid = jwk.Kid, use = "sig", alg = "RS256", n = jwk.N, e = jwk.E } } };
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref TokenRequests);
        var form = (await request.Content!.ReadAsStringAsync(cancellationToken))
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')), pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "", StringComparer.Ordinal);

        var (clientId, clientSecret) = ClientCredentials(request, form);
        if (clientId != ClientId || clientSecret != ClientSecret)
        {
            return Error(HttpStatusCode.Unauthorized, "invalid_client");
        }

        // O código vale uma vez: sai do dicionário na primeira troca, certa ou errada.
        if (form.GetValueOrDefault("grant_type") != "authorization_code" ||
            !_grants.TryRemove(form.GetValueOrDefault("code") ?? "", out var grant))
        {
            return Error(HttpStatusCode.BadRequest, "invalid_grant");
        }

        var verifier = form.GetValueOrDefault("code_verifier");
        if (string.IsNullOrEmpty(verifier) ||
            Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) != grant.Challenge)
        {
            return Error(HttpStatusCode.BadRequest, "invalid_grant");
        }

        var accessToken = $"ya29.{Guid.NewGuid():N}";
        _accessTokens[accessToken] = grant.Account;
        return Json(new
        {
            access_token = accessToken,
            expires_in = 3599,
            token_type = "Bearer",
            scope = "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/userinfo.profile",
            id_token = IdToken(grant.Account, grant.Nonce),
        });
    }

    private HttpResponseMessage Userinfo(HttpRequestMessage request)
    {
        var token = request.Headers.Authorization?.Parameter;
        if (token is null || !_accessTokens.TryGetValue(token, out var account))
        {
            return Error(HttpStatusCode.Unauthorized, "invalid_token");
        }

        var body = new Dictionary<string, object?> { ["sub"] = account.Subject };
        if (account.Email is not null)
        {
            body["email"] = account.Email;
            body["email_verified"] = account.EmailVerified;
        }

        if (account.HostedDomain is not null)
        {
            body["hd"] = account.HostedDomain;
        }

        return Json(body);
    }

    public string IdToken(Account account, string nonce)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = account.Subject,
            ["nonce"] = account.NonceOverride ?? nonce,
        };
        if (account.Email is not null)
        {
            claims["email"] = account.Email;
            claims["email_verified"] = account.EmailVerified;
        }

        if (account.HostedDomain is not null)
        {
            claims["hd"] = account.HostedDomain;
        }

        var key = account.SignWithAnotherKey ? OtherKey : SigningKey;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = account.IssuerOverride ?? Issuer,
            Audience = account.AudienceOverride ?? ClientId,
            IssuedAt = now.AddSeconds(-5),
            NotBefore = now.AddSeconds(-5),
            Expires = now + account.ExpiresIn,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });
    }

    private static (string? Id, string? Secret) ClientCredentials(HttpRequestMessage request, Dictionary<string, string> form)
    {
        if (request.Headers.Authorization is { Scheme: "Basic", Parameter: { } basic })
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(basic)).Split(':', 2);
            return (Uri.UnescapeDataString(decoded[0]), decoded.Length > 1 ? Uri.UnescapeDataString(decoded[1]) : null);
        }

        return (form.GetValueOrDefault("client_id"), form.GetValueOrDefault("client_secret"));
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: new JsonSerializerOptions()) };

    private static HttpResponseMessage Error(HttpStatusCode status, string error) =>
        new(status) { Content = JsonContent.Create(new { error }) };

    private sealed class Handler(FakeGoogle google) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            google.HandleAsync(request, cancellationToken);
    }
}
