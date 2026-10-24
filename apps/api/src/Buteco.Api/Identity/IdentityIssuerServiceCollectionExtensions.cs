using Buteco.Api.Identity.Keys;
using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Buteco.Api.Identity;

/// <summary>
/// Registro do emissor OIDC (design.md da change emissor-oidc-api, D2, D6, D7 e D8).
///
/// <para>
/// <b>Liga ou desliga pela url pública, só lendo configuração e sem lançar</b>: o
/// migration bundle roda o <c>Program.cs</c> até o <c>Build()</c>, e uma exceção aqui o
/// derrubaria. Quem reprova configuração errada é <c>ValidateIdentityIssuerConfiguration</c>,
/// depois do <c>Build()</c>.
/// </para>
/// <list type="bullet">
/// <item><c>PublicUrl:BaseUrl</c> vazia → nada do emissor é registrado, e o boot avisa;</item>
/// <item>presente mas inválida → só o estado é registrado, e o boot falha;</item>
/// <item>presente e válida → OpenIddict, anel de chaves, gate e rotação.</item>
/// </list>
/// </summary>
public static class IdentityIssuerServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityIssuer(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration[$"{PublicUrlOptions.SectionName}:{nameof(PublicUrlOptions.BaseUrl)}"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            services.AddSingleton(IdentityIssuerState.Disabled);
            return services;
        }

        services.Configure<IdentityIssuerOptions>(configuration.GetSection(IdentityIssuerOptions.SectionName));

        if (!IdentityIssuerState.TryParseIssuer(baseUrl, out var issuer))
        {
            services.AddSingleton(new IdentityIssuerState(Enabled: true, Issuer: null));
            return services;
        }

        services.AddSingleton(new IdentityIssuerState(Enabled: true, issuer));

        services.AddSingleton<IdentityKeyMaterialCipher>();
        services.AddSingleton<IdentityKeyFactory>();
        services.AddSingleton<IdentityKeyRing>();
        services.AddSingleton(new IdentityKeyRotationSchedule());
        services.AddHostedService<IdentityKeyRotationService>();
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, IdentityKeyRingOptionsConfigurer>();
        services.AddSingleton<IOptionsChangeTokenSource<OpenIddictServerOptions>, IdentityKeyRingChangeTokenSource>();

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<AppDbContext>())
            .AddServer(server =>
            {
                server.SetIssuer(issuer);

                // Explícitas, inclusive as duas que o OpenIddict já traz por padrão: o
                // padrão do discovery inclui também /.well-known/oauth-authorization-server,
                // que não está na lista esperada.
                server.SetConfigurationEndpointUris(Relative(IdentityIssuerEndpoints.Configuration));
                server.SetJsonWebKeySetEndpointUris(Relative(IdentityIssuerEndpoints.JsonWebKeySet));
                server.SetTokenEndpointUris(Relative(IdentityIssuerEndpoints.Token));

                server.AllowClientCredentialsFlow();

                // O access token sai JWT assinado e não cifrado: quem valida (a #208) só
                // tem o JWKS. Os outros tipos continuam cifrados, como o OpenIddict exige.
                server.DisableAccessTokenEncryption();

                server.AddEventHandler<HandleTokenRequestContext>(handler =>
                    handler.UseScopedHandler<ClientCredentialsTokenHandler>());

                // O issuer fixo não basta: sem este handler, os endpoints do discovery
                // sairiam do esquema e do host da requisição (D8, correção).
                server.AddEventHandler(DiscoveryEndpointsFromIssuerHandler.Descriptor);

                // TLS termina fora do stack, e o apps/api recebe HTTP puro (D8).
                server.UseAspNetCore().DisableTransportSecurityRequirement();
            });

        return services;
    }

    /// <summary>
    /// Gate do anel antes do <c>UseAuthentication</c> (D6). Não faz nada com o emissor
    /// desligado.
    /// </summary>
    public static IApplicationBuilder UseIdentityKeyRingGate(this IApplicationBuilder app)
    {
        if (app.ApplicationServices.GetRequiredService<IdentityIssuerState>().Issuer is null)
        {
            return app;
        }

        return app.UseMiddleware<IdentityKeyRingGate>();
    }

    private static string Relative(string path) => path.TrimStart('/');
}

/// <summary>O que o registro decidiu, para o boot e o gate lerem.</summary>
public sealed record IdentityIssuerState(bool Enabled, Uri? Issuer)
{
    public static readonly IdentityIssuerState Disabled = new(Enabled: false, Issuer: null);

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
