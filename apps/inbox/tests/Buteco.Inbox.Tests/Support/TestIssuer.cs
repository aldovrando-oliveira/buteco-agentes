using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Buteco.Inbox.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Inbox.Tests.Support;

/// <summary>
/// Emissor de mentira para os testes da validação do token do emissor (change
/// validacao-jwks-inbox-connectors, D9), sem o <c>apps/api</c> real. Gera chaves RSA com
/// <c>kid</c> próprio, emite access tokens com os claims controláveis, e serve o discovery
/// e o JWKS por um <see cref="HttpMessageHandler"/> posto como handler primário do cliente
/// <c>IssuerKeys</c>. Conta e guarda cada requisição, publica e retira chaves, e pode
/// ficar fora do ar ou responder <c>503</c>.
/// <para>
/// Cópia no <c>apps/connectors</c> (<c>Buteco.Connectors.Tests/Support/TestIssuer.cs</c>),
/// sem projeto de teste compartilhado (convenção 7).
/// </para>
/// </summary>
public sealed class TestIssuer
{
    public const string Issuer = "https://issuer.exemplo.test/";
    public const string InternalBaseUrl = "http://apps-api.test";

    private readonly Lock _gate = new();
    private readonly List<RsaSecurityKey> _published = [];
    private readonly List<Uri> _requests = [];

    public TestIssuer()
    {
        SigningKey = CreateKey();
        Publish(SigningKey);
    }

    /// <summary>Chave publicada desde o início, usada por padrão em <see cref="Issue"/>.</summary>
    public RsaSecurityKey SigningKey { get; }

    public IssuerAvailability Availability { get; set; } = IssuerAvailability.Online;

    /// <summary>Issuer que o discovery declara; por padrão, o <see cref="Issuer"/>.</summary>
    public string AnnouncedIssuer { get; set; } = Issuer;

    public HttpMessageHandler CreateHandler() => new Handler(this);

    public IReadOnlyList<Uri> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public int DiscoveryRequests => Requests.Count(uri => uri.AbsolutePath == IssuerKeysRetriever.DiscoveryPath);

    public int JwksRequests => Requests.Count(uri => uri.AbsolutePath == IssuerKeysRetriever.JwksPath);

    public static RsaSecurityKey CreateKey() =>
        new(RSA.Create(2048)) { KeyId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(12)) };

    public void Publish(RsaSecurityKey key)
    {
        lock (_gate)
        {
            _published.Add(key);
        }
    }

    public void Retire(RsaSecurityKey key)
    {
        lock (_gate)
        {
            _published.Remove(key);
        }
    }

    /// <summary>
    /// Access token do emissor. Sem argumentos: o token do painel, válido para o
    /// <c>apps/inbox</c>. <paramref name="clientId"/> nulo omite o claim.
    /// </summary>
    public string Issue(
        string[]? audiences = null,
        string? clientId = IssuerClientSubjects.PanelClientId,
        string subject = "usuario-teste",
        string issuer = Issuer,
        DateTime? expires = null,
        SecurityKey? key = null,
        string algorithm = SecurityAlgorithms.RsaSha256,
        string tokenType = IssuerTokenAuthentication.AccessTokenType)
    {
        audiences ??= [IssuerTokenAudience.Value];
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(30);
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = subject,
            [JwtRegisteredClaimNames.Aud] = audiences.Length == 1 ? audiences[0] : audiences,
        };
        if (clientId is not null)
        {
            claims[IssuerTokenAuthentication.ClientIdClaim] = clientId;
        }

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Claims = claims,
            IssuedAt = expiresAt.AddHours(-1),
            NotBefore = expiresAt.AddHours(-1),
            Expires = expiresAt,
            TokenType = tokenType,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, algorithm),
        });
    }

    /// <summary>JWS sem assinatura (<c>alg</c> <c>none</c>), com os claims do token do painel.</summary>
    public static string IssueUnsigned()
    {
        var header = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new { alg = "none", typ = IssuerTokenAuthentication.AccessTokenType }));
        var payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = IssuerTokenAudience.Value,
            ["sub"] = "usuario-teste",
            ["client_id"] = IssuerClientSubjects.PanelClientId,
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(),
        }));
        return $"{header}.{payload}.";
    }

    private string DiscoveryDocument() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["issuer"] = AnnouncedIssuer,
        // Anunciado sob o issuer público, como o apps/api faz: o app nunca segue este valor.
        ["jwks_uri"] = Issuer + ".well-known/jwks",
        ["token_endpoint"] = Issuer + "connect/token",
    });

    private string JwksDocument()
    {
        List<RsaSecurityKey> keys;
        lock (_gate)
        {
            keys = [.. _published];
        }

        return JsonSerializer.Serialize(new
        {
            keys = keys.Select(key =>
            {
                var parameters = key.Rsa.ExportParameters(includePrivateParameters: false);
                return new Dictionary<string, string>
                {
                    ["kty"] = "RSA",
                    ["use"] = "sig",
                    ["alg"] = SecurityAlgorithms.RsaSha256,
                    ["kid"] = key.KeyId,
                    ["n"] = Base64UrlEncoder.Encode(parameters.Modulus!),
                    ["e"] = Base64UrlEncoder.Encode(parameters.Exponent!),
                };
            }),
        });
    }

    private sealed class Handler(TestIssuer issuer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (issuer._gate)
            {
                issuer._requests.Add(request.RequestUri!);
            }

            switch (issuer.Availability)
            {
                case IssuerAvailability.Unreachable:
                    throw new HttpRequestException("emissor inalcançável (TestIssuer)");
                case IssuerAvailability.ServiceUnavailable:
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var body = request.RequestUri!.AbsolutePath switch
            {
                IssuerKeysRetriever.DiscoveryPath => issuer.DiscoveryDocument(),
                IssuerKeysRetriever.JwksPath => issuer.JwksDocument(),
                _ => null,
            };

            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

public enum IssuerAvailability
{
    Online,
    Unreachable,
    ServiceUnavailable,
}
