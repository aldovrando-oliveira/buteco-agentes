using System.Security.Claims;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Keys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Api.Identity.Validation;

/// <summary>
/// Autenticação das rotas do <c>apps/api</c> (design.md da change
/// usuarios-locais-login-emissor, D3.3): o token HMAC de sempre e o access token do próprio
/// emissor, no desenho do <c>apps/inbox</c> e do <c>apps/connectors</c> (#208), sem
/// <c>libs/</c>.
/// <list type="bullet">
/// <item><b>Um esquema por token, escolhido pela forma:</b> o esquema de política
/// <see cref="PolicySchemeName"/> manda o JWS compacto (exatamente dois pontos) ao
/// <see cref="IssuerSchemeName"/> e qualquer outra coisa, inclusive a requisição sem token,
/// ao <see cref="OperatorTokenAuthenticationHandler"/>, que não muda. O HMAC tem um ponto
/// por construção. Um JWT inválido nunca é tentado no HMAC.</item>
/// <item><b>Chaves do anel, sem HTTP:</b> o <c>IssuerSigningKeyResolver</c> lê as chaves de
/// validação do <see cref="IdentityKeyRing.Snapshot"/>, que já inclui a anterior em
/// retenção e a próxima publicada. O <c>IdentityKeyRingGate</c> já responde <c>503</c> até
/// o anel carregar, e a troca de chave já recarrega o snapshot.</item>
/// <item><b>O subject sai da tabela de clientes</b> (<see cref="IssuerClientSubjects"/>),
/// nunca do <c>sub</c>.</item>
/// </list>
/// </summary>
public static class IssuerTokenAuthentication
{
    public const string PolicySchemeName = "Bearer";
    public const string IssuerSchemeName = "IssuerToken";

    // typ do access token do OpenIddict: recusa um id_token do mesmo emissor.
    public const string AccessTokenType = "at+jwt";

    public const string ClientIdClaim = "client_id";
    public const string SubjectClaim = "sub";

    public static IServiceCollection AddIssuerTokenAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(PolicySchemeName)
            .AddPolicyScheme(PolicySchemeName, displayName: null, options => options.ForwardDefaultSelector = SelectScheme)
            .AddScheme<AuthenticationSchemeOptions, OperatorTokenAuthenticationHandler>(
                OperatorTokenAuthenticationHandler.SchemeName, _ => { })
            .AddJwtBearer(IssuerSchemeName, _ => { });

        services
            .AddOptions<JwtBearerOptions>(IssuerSchemeName)
            .Configure<IdentityKeyRing, IdentityIssuerState>((options, ring, issuer) =>
            {
                var issuerUri = issuer.Issuer?.AbsoluteUri;

                // Nenhum claim renomeado em silêncio: sub e client_id chegam como estão.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuers = issuerUri is null ? [] : [issuerUri, issuerUri.TrimEnd('/')],
                    ValidAudience = IssuerTokenAudience.Value,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidTypes = [AccessTokenType],
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    IssuerSigningKeyResolver = (_, _, _, _) => ring.Snapshot?.SigningKeys ?? [],
                };
                options.Events = new JwtBearerEvents { OnTokenValidated = MapPrincipalByClient };
            });

        return services;
    }

    public static string SelectScheme(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && header.AsSpan("Bearer ".Length).Trim().Count('.') == 2)
        {
            return IssuerSchemeName;
        }

        return OperatorTokenAuthenticationHandler.SchemeName;
    }

    // O principal é reconstruído. NameIdentifier só pela tabela de clientes; o sub nunca
    // vira subject. Cliente ausente ou fora da tabela autentica sem NameIdentifier, e o
    // ServiceScopeAuthorizationHandler responde 403.
    private static Task MapPrincipalByClient(TokenValidatedContext context)
    {
        var clientId = context.Principal?.FindFirst(ClientIdClaim)?.Value;
        var subject = context.Principal?.FindFirst(SubjectClaim)?.Value;

        var claims = new List<Claim>();
        if (clientId is not null && IssuerClientSubjects.Table.TryGetValue(clientId, out var mappedSubject))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, mappedSubject));
        }

        if (clientId is not null)
        {
            claims.Add(new Claim(ClientIdClaim, clientId));
        }

        if (subject is not null)
        {
            claims.Add(new Claim(SubjectClaim, subject));
        }

        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, IssuerSchemeName));
        return Task.CompletedTask;
    }
}
