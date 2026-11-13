using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.Federated;
using Buteco.Api.Identity.Flow;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Identity.Storage;
using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Buteco.Api.Identity;

/// <summary>
/// Registro do emissor OIDC (design.md da change emissor-oidc-api, D2, D6, D7 e D8).
///
/// <para>
/// <b>A url pública é obrigatória</b> (design.md da change usuarios-locais-login-emissor,
/// D3.4): o login de usuário depende do emissor, e sem issuer o app subiria sem nenhuma
/// forma de entrar depois da #217. <b>O registro só lê configuração e não lança</b>: o
/// migration bundle roda o <c>Program.cs</c> até o <c>Build()</c>, e uma exceção aqui o
/// derrubaria. Quem reprova url ausente ou inválida é
/// <c>ValidateIdentityIssuerConfiguration</c>, depois do <c>Build()</c>.
/// </para>
/// <list type="bullet">
/// <item><c>PublicUrl:BaseUrl</c> ausente, vazia ou inválida → só o estado, sem issuer, é
/// registrado, e o boot falha;</item>
/// <item>válida → OpenIddict, anel de chaves, gate, rotação, limpeza das entradas de token
/// (change limpeza-tokens-openiddict, D1 e D4), sessão do emissor e catálogo de clientes.</item>
/// </list>
/// </summary>
public static class IdentityIssuerServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityIssuer(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration[$"{PublicUrlOptions.SectionName}:{nameof(PublicUrlOptions.BaseUrl)}"];

        services.Configure<IdentityIssuerOptions>(configuration.GetSection(IdentityIssuerOptions.SectionName));

        // Os provedores federados (design.md da change provedores-federados-google, D2): lidos aqui,
        // sem lançar, porque as registrações do cliente precisam deles antes do Build(); a checagem é
        // ValidateFederatedProvidersConfiguration, depois dele. Registrado antes do retorno sem issuer:
        // as rotas de usuários e de Minha conta o recebem por injeção.
        var federated = FederatedProviderCatalog.Read(configuration);
        services.AddSingleton(federated);

        if (string.IsNullOrWhiteSpace(baseUrl) || !IdentityIssuerState.TryParseIssuer(baseUrl, out var issuer))
        {
            services.AddSingleton(new IdentityIssuerState(Issuer: null));
            return services;
        }

        services.AddSingleton(new IdentityIssuerState(issuer));

        // A sessão do emissor, que o POST /auth/session grava e o authorize lê
        // (usuarios-locais-login-emissor, D1).
        services.AddIssuerSessionCookie();
        services.AddScoped<IIdentityClientCatalog, IdentityClientCatalog>();

        services.AddSingleton<IdentityKeyMaterialCipher>();
        services.AddSingleton<IdentityKeyFactory>();
        services.AddSingleton<IdentityKeyRing>();
        services.AddSingleton(new IdentityKeyRotationSchedule());
        services.AddHostedService<IdentityKeyRotationService>();
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, IdentityKeyRingOptionsConfigurer>();
        services.AddSingleton<IOptionsChangeTokenSource<OpenIddictServerOptions>, IdentityKeyRingChangeTokenSource>();
        services.AddSingleton<IIdentityTokenPruner, IdentityTokenPruner>();
        services.AddSingleton(new IdentityTokenPruneSchedule());
        services.AddHostedService<IdentityTokenPruneService>();

        services.AddMemoryCache();
        services.AddSingleton<FederatedRefusalCookie>();
        var panelBase = Uri.TryCreate(configuration[$"{IdentityIssuerOptions.SectionName}:{nameof(IdentityIssuerOptions.PanelBaseUrl)}"], UriKind.Absolute, out var panel)
            ? panel
            : issuer;
        var panelRoot = new Uri(panelBase.GetLeftPart(UriPartial.Authority) + "/");
        var federatedRedirectUri = new Uri(panelRoot, FederatedLogin.CallbackPath.TrimStart('/'));
        services.AddSingleton(new FederatedRedirect(panelRoot, federatedRedirectUri));

        var openIddict = services.AddOpenIddict();
        openIddict
            .AddCore(core =>
            {
                core.UseEntityFrameworkCore().UseDbContext<AppDbContext>();

                // A aplicação externa autenticada pelos segredos dela (design.md da change
                // aplicacoes-externas-credencial, D4); os outros clientes seguem o OpenIddict.
                core.ReplaceApplicationManager(typeof(ButecoApplicationManager<>));
            })
            .AddServer(server =>
            {
                server.SetIssuer(issuer);

                // Explícitas, inclusive as duas que o OpenIddict já traz por padrão: o
                // padrão do discovery inclui também /.well-known/oauth-authorization-server,
                // que não está na lista esperada.
                server.SetConfigurationEndpointUris(Relative(IdentityIssuerEndpoints.Configuration));
                server.SetJsonWebKeySetEndpointUris(Relative(IdentityIssuerEndpoints.JsonWebKeySet));
                server.SetTokenEndpointUris(Relative(IdentityIssuerEndpoints.Token));
                server.SetAuthorizationEndpointUris(Relative(IdentityIssuerEndpoints.Authorization));
                server.SetEndSessionEndpointUris(Relative(IdentityIssuerEndpoints.EndSession));

                // O fluxo do usuário (usuarios-locais-login-emissor, D1): authorization code
                // com PKCE obrigatório para todo cliente, e refresh rotativo e deslizante,
                // que é o padrão do OpenIddict (2.4 (d)).
                server.AllowClientCredentialsFlow();
                server.AllowAuthorizationCodeFlow();
                server.AllowRefreshTokenFlow();
                server.RequireProofKeyForCodeExchange();

                // D3.7: 15 min para todo access token, inclusive o de client_credentials
                // (#186), e 14 dias de refresh, a idade mínima da limpeza. A retenção das
                // chaves cobre a maior das duas (6.9). Todas as durações vêm de
                // IdentityTokenLifetimes, inclusive o id token e o código, que são os padrões
                // do OpenIddict ditos explicitamente (leitura-identidade-emissor, D6).
                server.SetAccessTokenLifetime(IdentityTokenLifetimes.AccessToken);
                server.SetRefreshTokenLifetime(IdentityTokenLifetimes.RefreshToken);
                server.SetIdentityTokenLifetime(IdentityTokenLifetimes.IdentityToken);
                server.SetAuthorizationCodeLifetime(IdentityTokenLifetimes.AuthorizationCode);

                server.RegisterScopes([Scopes.OpenId, Scopes.OfflineAccess, .. IdentityClients.Scopes]);

                // O access token sai JWT assinado e não cifrado: quem valida (a #208) só
                // tem o JWKS. Os outros tipos continuam cifrados, como o OpenIddict exige.
                server.DisableAccessTokenEncryption();

                server.AddEventHandler<HandleTokenRequestContext>(handler =>
                    handler.UseScopedHandler<ClientCredentialsTokenHandler>());

                // Todo token novo de usuário confere estado e stamp (D4).
                server.AddEventHandler<HandleTokenRequestContext>(handler =>
                    handler.UseScopedHandler<UserGrantTokenHandler>());

                // O logout recusado pelo OpenIddict também apaga o cookie (D3.9, 2.4 (b)).
                server.AddEventHandler(EndSessionErrorHandler.Descriptor);

                // O issuer fixo não basta: sem este handler, os endpoints do discovery
                // sairiam do esquema e do host da requisição (D8, correção).
                server.AddEventHandler(DiscoveryEndpointsFromIssuerHandler.Descriptor);

                // Nenhum access token e nenhum id token sai com linha em openiddict_tokens:
                // ninguém a lê (limpeza-tokens-openiddict, D1; usuarios-locais-login-emissor,
                // D3.8). Código e refresh continuam gravados.
                server.AddEventHandler(TokenEntryStorageHandler.Descriptor);

                // TLS termina fora do stack, e o apps/api recebe HTTP puro (D8). O authorize
                // e o logout em passthrough, SEM EnableErrorPassthrough: ele é global e
                // mandaria à rota os erros do authorize (2.4 (a) e (b)).
                server.UseAspNetCore()
                    .DisableTransportSecurityRequirement()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
            });

        // O cliente OIDC só existe com provedor configurado: sem nenhum, o login fica só com senha,
        // exatamente como antes da change (spec federated-providers), e nenhum handler do cliente entra
        // no caminho das requisições.
        if (federated.Providers.Count == 0)
        {
            return services;
        }

        services.AddSingleton<IConfigureOptions<OpenIddictClientOptions>, IdentityKeyRingClientOptionsConfigurer>();
        services.AddSingleton<IOptionsChangeTokenSource<OpenIddictClientOptions>, IdentityKeyRingClientChangeTokenSource>();

        openIddict.AddClient(client =>
            {
                // O apps/api como cliente OIDC dos provedores federados, atrás do emissor (design.md da
                // change provedores-federados-google, D1 e D4): authorization code com PKCE e nonce; o
                // state token de 10 min, de uso único pela entrada em openiddict_tokens; o retorno em
                // passthrough, para a rota decidir. As credenciais vêm do anel
                // (IdentityKeyRingClientOptionsConfigurer).
                client.AllowAuthorizationCodeFlow();
                client.SetRedirectionEndpointUris(Relative(FederatedLogin.CallbackPath));
                client.SetStateTokenLifetime(FederatedLogin.FlowLifetime);

                // Sem esquema encaminhado por provedor: o apps/api registra só o JwtBearer como padrão,
                // e nenhum provedor vira esquema de autenticação de rota. Com o passthrough dos erros, toda
                // falha do retorno (correlação, state, código, id token, error do provedor) chega à rota,
                // que responde a recusa falha em vez da página de erro do OpenIddict; o cliente só tem o
                // endpoint do retorno, e o passthrough não alcança mais nada.
                client.UseAspNetCore()
                    .DisableTransportSecurityRequirement()
                    .DisableAutomaticAuthenticationSchemeForwarding()
                    .EnableRedirectionEndpointPassthrough()
                    .EnableErrorPassthrough();

                client.UseSystemNetHttp();

                // A URI do retorno pela base do painel, e não pelo host da requisição (registro do apply).
                client.AddEventHandler(FederatedRequestUriHandler.Descriptor);

                var web = client.UseWebProviders();
                foreach (var provider in federated.Providers)
                {
                    // Só o tipo Google na M3: o issuer, os escopos e o prompt são fixos do tipo.
                    web.AddGoogle(google => google
                        .SetProviderName(provider.Key)
                        .SetRegistrationId(provider.Key)
                        .SetClientId(provider.ClientId)
                        .SetClientSecret(provider.ClientSecret)
                        .SetRedirectUri(federatedRedirectUri)
                        .AddScopes([.. FederatedProviderTypes.GoogleScopes])
                        .SetPrompt(FederatedProviderTypes.GooglePrompt));
                }
            });

        // O cookie do fluxo (D4): Lax, só no caminho do retorno, Secure fora de Development, como o
        // buteco-sessao. O ambiente só existe no contêiner, e o registro não o recebe.
        services.AddOptions<OpenIddictClientAspNetCoreOptions>().Configure<IHostEnvironment>((options, environment) =>
        {
            options.CookieBuilder.Name = FederatedLogin.FlowCookieName;
            options.CookieBuilder.SameSite = SameSiteMode.Lax;
            options.CookieBuilder.Path = FederatedLogin.CallbackPath;
            options.CookieBuilder.HttpOnly = true;
            options.CookieBuilder.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });

        return services;
    }

    /// <summary>Gate do anel antes do <c>UseAuthentication</c> (D6).</summary>
    public static IApplicationBuilder UseIdentityKeyRingGate(this IApplicationBuilder app) =>
        app.UseMiddleware<IdentityKeyRingGate>();

    private static string Relative(string path) => path.TrimStart('/');
}

/// <summary>
/// O issuer que o registro leu da url pública, ou nulo quando ela está ausente ou inválida,
/// caso em que o boot falha (<c>ValidateIdentityIssuerConfiguration</c>).
/// </summary>
public sealed record IdentityIssuerState(Uri? Issuer)
{
    /// <summary>
    /// Url absoluta <c>http</c>/<c>https</c>, sem query nem fragmento. O issuer é ela com
    /// exatamente uma barra final.
    /// </summary>
    public static bool TryParseIssuer(string baseUrl, out Uri issuer)
    {
        issuer = null!;

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        issuer = new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        return true;
    }
}
