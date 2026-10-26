using System.Globalization;
using System.Security.Claims;
using Buteco.Inbox.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Inbox.Auth;

// Duplicado deliberadamente de
// apps/connectors/src/Buteco.Connectors/Auth/IssuerTokenAuthentication.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

/// <summary>
/// Autenticação do <c>apps/inbox</c>: o token HMAC de sempre e, com
/// <c>Identity:Issuer</c> configurado, o access token do emissor do <c>apps/api</c>
/// validado pelo JWKS (change validacao-jwks-inbox-connectors).
/// <para>
/// <b>Um esquema por token, escolhido pela forma (D2):</b> o esquema de política
/// <see cref="PolicySchemeName"/> manda o JWS compacto (exatamente dois pontos) ao
/// <see cref="IssuerSchemeName"/> e qualquer outra coisa, inclusive a requisição sem
/// token, ao <see cref="OperatorTokenAuthenticationHandler"/>, que não muda. O HMAC tem
/// um ponto por construção, então as formas não se sobrepõem. O esquema escolhido é o
/// único que roda: um JWT inválido nunca é tentado no HMAC, e um HMAC inválido nunca toca
/// nas chaves do emissor. Desafio e recusa seguem o mesmo seletor.
/// </para>
/// <para>
/// <b>Sem <c>Identity:Issuer</c> (D7):</b> registra só o HMAC, como antes desta change;
/// o aviso sai no boot, por <see cref="IssuerTokenStartupValidation"/>. O registro não
/// confere o valor, e não lança: quem reprova issuer inválido é a checagem de boot, e é
/// ela que o teste da composição real guarda.
/// </para>
/// </summary>
public static class IssuerTokenAuthentication
{
    public const string PolicySchemeName = "Bearer";
    public const string IssuerSchemeName = "IssuerToken";

    // typ do access token do OpenIddict (F9): recusa um id_token do mesmo emissor.
    public const string AccessTokenType = "at+jwt";

    // Claims do token do emissor que ficam no principal reconstruído, para log (D6).
    public const string ClientIdClaim = "client_id";
    public const string SubjectClaim = "sub";

    public static IServiceCollection AddIssuerTokenAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IssuerTokenOptions>(configuration.GetSection(IssuerTokenOptions.SectionName));

        var issuerOptions = configuration.GetSection(IssuerTokenOptions.SectionName).Get<IssuerTokenOptions>() ?? new IssuerTokenOptions();
        if (!issuerOptions.Enabled)
        {
            services
                .AddAuthentication(OperatorTokenAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, OperatorTokenAuthenticationHandler>(
                    OperatorTokenAuthenticationHandler.SchemeName, _ => { });
            return services;
        }

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

        // Timeout curto e fixo, o mesmo do AgentReferenceValidator. Sem o
        // ServiceTokenDelegatingHandler: discovery e JWKS são anônimos.
        services.AddHttpClient(IssuerKeysRetriever.HttpClientName, (provider, client) =>
        {
            client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl!);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHostedService<IssuerKeysRefreshService>();

        services
            .AddAuthentication(PolicySchemeName)
            .AddPolicyScheme(PolicySchemeName, displayName: null, options => options.ForwardDefaultSelector = SelectScheme)
            .AddScheme<AuthenticationSchemeOptions, OperatorTokenAuthenticationHandler>(
                OperatorTokenAuthenticationHandler.SchemeName, _ => { })
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

    // D6: o principal é reconstruído. NameIdentifier só pela tabela de clientes; o sub
    // nunca vira subject. Cliente ausente ou fora da tabela autentica sem
    // NameIdentifier, e a SubjectAuthorization responde 403.
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

    // D4: sem nenhuma chave obtida, o desafio vira 503 com Retry-After. Com chave
    // obtida, falha de validação é sempre 401.
    private static Task ChallengeWithoutKeysAsUnavailable(JwtBearerChallengeContext context)
    {
        if (context.HttpContext.RequestServices.GetRequiredService<IssuerKeysState>().Obtained)
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
