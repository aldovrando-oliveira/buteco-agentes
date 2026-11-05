using System.Globalization;
using System.Security.Claims;
using Buteco.Connectors.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Connectors.Auth;

// Duplicado deliberadamente de
// apps/inbox/src/Buteco.Inbox/Auth/IssuerTokenAuthentication.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

/// <summary>
/// Autenticação do <c>apps/connectors</c>: o access token do emissor do <c>apps/api</c>,
/// validado pelo JWKS (change validacao-jwks-inbox-connectors), num esquema só,
/// <see cref="IssuerSchemeName"/>, que é o padrão (change remover-hmac-token-servico, D1).
/// <para>
/// <b>Sem <c>Identity:Issuer</c>, o app não sobe</b> (D2): quem reprova é a checagem de
/// boot, <see cref="IssuerTokenStartupValidation"/>. O registro não confere o valor, e é
/// a checagem que o teste da composição real guarda.
/// </para>
/// <para>
/// <b>Desafio (D3):</b> antes da primeira obtenção das chaves, a requisição que traz
/// <c>Authorization</c> recebe <c>503</c> com <c>Retry-After</c>. A que não traz recebe o
/// <c>401</c> padrão, com <c>WWW-Authenticate: Bearer</c>, com ou sem chaves: a resposta
/// dela não depende das chaves.
/// </para>
/// </summary>
public static class IssuerTokenAuthentication
{
    public const string IssuerSchemeName = "IssuerToken";

    // typ do access token do OpenIddict (F9): recusa um id_token do mesmo emissor.
    public const string AccessTokenType = "at+jwt";

    // Claims do token do emissor que ficam no principal reconstruído, para log (D6).
    public const string ClientIdClaim = "client_id";
    public const string SubjectClaim = "sub";

    public static IServiceCollection AddIssuerTokenAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IssuerTokenOptions>(configuration.GetSection(IssuerTokenOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new IssuerKeysSchedule());
        services.AddSingleton<IssuerKeysState>();
        services.AddSingleton<IssuerKeysRetriever>();
        services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(provider =>
        {
            var schedule = provider.GetRequiredService<IssuerKeysSchedule>();
            var apiBaseUrl = provider.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl!;

            // O endereço só aparece nos logs da biblioteca; quem busca é o retriever, pelo
            // cliente nomeado. O HttpDocumentRetriever padrão nunca é usado.
            return new ConfigurationManager<OpenIdConnectConfiguration>(
                apiBaseUrl.TrimEnd('/') + IssuerKeysRetriever.DiscoveryPath,
                provider.GetRequiredService<IssuerKeysRetriever>(),
                new HttpDocumentRetriever())
            {
                AutomaticRefreshInterval = schedule.AutomaticRefreshInterval,
                RefreshInterval = schedule.UnknownKidRefreshInterval,
                // Sem isso, um token assinado por chave já retirada do JWKS continuaria
                // validando pela configuração anterior guardada como "last known good",
                // por até 1 h (TokenUtilities.IsRecoverableConfiguration, IdentityModel
                // 8.19.2). A manutenção das chaves quando a renovação falha não depende
                // disto (F2).
                UseLastKnownGoodConfiguration = false,
            };
        });

        // Timeout curto e fixo, o mesmo do AgentReferenceValidator do apps/inbox. Sem o
        // ServiceTokenDelegatingHandler: discovery e JWKS são anônimos.
        services.AddHttpClient(IssuerKeysRetriever.HttpClientName, (provider, client) =>
        {
            client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl!);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHostedService<IssuerKeysRefreshService>();

        services
            .AddAuthentication(IssuerSchemeName)
            .AddJwtBearer(IssuerSchemeName, _ => { });

        services
            .AddOptions<JwtBearerOptions>(IssuerSchemeName)
            .Configure<IConfigurationManager<OpenIdConnectConfiguration>, IOptions<IssuerTokenOptions>>(
                (options, configurationManager, configuredIssuer) =>
                {
                    options.ConfigurationManager = configurationManager;
                    options.RefreshOnIssuerKeyNotFound = true;
                    // Nenhum claim renomeado em silêncio: sub e client_id chegam como estão.
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = configuredIssuer.Value.NormalizedIssuer,
                        ValidAudience = IssuerTokenAudience.Value,
                        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                        ValidTypes = [AccessTokenType],
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                    };
                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = MapPrincipalByClient,
                        OnChallenge = ChallengeWithoutKeysAsUnavailable,
                    };
                });

        return services;
    }

    // D6: o principal é reconstruído. NameIdentifier só pela tabela de clientes; o sub
    // nunca vira subject. Cliente ausente ou fora da tabela autentica sem
    // NameIdentifier, e o ConnectorsSubjectAuthorizationHandler responde 403.
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

    // D4: sem nenhuma chave obtida, o desafio de quem traz Authorization vira 503 com
    // Retry-After. Com chave obtida, falha de validação é sempre 401. Sem Authorization,
    // sempre o 401 padrão, com WWW-Authenticate: Bearer (remover-hmac-token-servico, D3).
    private static Task ChallengeWithoutKeysAsUnavailable(JwtBearerChallengeContext context)
    {
        if (StringValues.IsNullOrEmpty(context.Request.Headers.Authorization)
            || context.HttpContext.RequestServices.GetRequiredService<IssuerKeysState>().Obtained)
        {
            return Task.CompletedTask;
        }

        var schedule = context.HttpContext.RequestServices.GetRequiredService<IssuerKeysSchedule>();
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(schedule.FailedFetchBackoff.TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);
        return Task.CompletedTask;
    }
}
