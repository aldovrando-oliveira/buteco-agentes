using System.Security.Claims;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Api.Identity.Validation;

/// <summary>
/// Autenticação das rotas do <c>apps/api</c> (design.md da change
/// usuarios-locais-login-emissor, D3.3): o access token do próprio emissor, no desenho do
/// <c>apps/inbox</c> e do <c>apps/connectors</c> (#208), sem <c>libs/</c>.
/// <list type="bullet">
/// <item><b>Um esquema só</b>, <see cref="IssuerSchemeName"/>, que é o padrão (change
/// remover-hmac-token-servico, D1). A requisição sem token recebe o <c>401</c> padrão do
/// <c>JwtBearer</c>, com <c>WWW-Authenticate: Bearer</c> (D3).</item>
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
    public const string IssuerSchemeName = "IssuerToken";

    // typ do access token do OpenIddict: recusa um id_token do mesmo emissor.
    public const string AccessTokenType = "at+jwt";

    public const string ClientIdClaim = "client_id";
    public const string SubjectClaim = "sub";

    /// <summary>
    /// O id da autorização do OpenIddict, que o access token sempre leva e que não muda entre
    /// renovações (design.md da change rotas-conta-usuarios, H1–H3). É a sessão do token: a
    /// lista de Minha conta marca a atual por ele, e "encerrar as outras" o poupa.
    /// </summary>
    public const string AuthorizationIdClaim = "oi_au_id";

    /// <summary>O papel do token do painel (design.md da change papeis-no-token, D3), no texto de <see cref="UserRoleText"/>.</summary>
    public const string RoleClaim = "role";

    public static IServiceCollection AddIssuerTokenAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(IssuerSchemeName)
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

    // O token do painel sem role válido FALHA a autenticação (design.md da change papeis-no-token,
    // D3, Q12), e o 401 sai pelo desafio do JwtBearer, sem corpo problem+json: é o que faz o painel
    // renovar e repetir, e a renovação traz o papel gravado. Com 403, os access tokens de antes do
    // deploy ficariam até 15 min sem acesso.
    private static Task MapPrincipalByClient(TokenValidatedContext context)
    {
        if (context.Principal?.FindFirst(ClientIdClaim)?.Value == IdentityClients.PanelClientId &&
            UserRoleText.TryRead(context.Principal.FindFirst(RoleClaim)?.Value) is null)
        {
            context.Fail("O token do painel não traz um papel conhecido.");
            return Task.CompletedTask;
        }

        context.Principal = MapPrincipal(context.Principal);
        return Task.CompletedTask;
    }

    // O principal é reconstruído. NameIdentifier só pela tabela de clientes; o sub nunca
    // vira subject. Cliente ausente ou fora da tabela autentica sem NameIdentifier, e o
    // ServiceScopeAuthorizationHandler responde 403. O sub e o oi_au_id vão como estão, para
    // as rotas que agem sobre a própria pessoa e a própria sessão (CurrentUser, Minha conta). O role
    // vai só para o token do painel: os clientes de serviço são autorizados pela tabela de subjects,
    // e um role no token deles não vale nada (papeis-no-token, D2).
    public static ClaimsPrincipal MapPrincipal(ClaimsPrincipal? validated)
    {
        var clientId = validated?.FindFirst(ClientIdClaim)?.Value;
        var subject = validated?.FindFirst(SubjectClaim)?.Value;
        var authorizationId = validated?.FindFirst(AuthorizationIdClaim)?.Value;
        var role = validated?.FindFirst(RoleClaim)?.Value;

        var claims = new List<Claim>();
        if (clientId == IdentityClients.PanelClientId && role is not null)
        {
            claims.Add(new Claim(RoleClaim, role));
        }

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

        if (authorizationId is not null)
        {
            claims.Add(new Claim(AuthorizationIdClaim, authorizationId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, IssuerSchemeName));
    }
}
