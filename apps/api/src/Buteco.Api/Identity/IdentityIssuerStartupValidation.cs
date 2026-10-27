using Buteco.Api.Identity.Keys;
using Buteco.Api.Identity.Storage;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Buteco.Api.Identity;

/// <summary>
/// Checagens de boot do emissor OIDC (convenção 8; design.md da change emissor-oidc-api,
/// D8), chamadas depois do <c>Build()</c>, no molde de <c>ValidateConnectorsConfiguration</c>.
/// Nenhuma consulta o banco.
/// </summary>
public static class IdentityIssuerStartupValidation
{
    /// <summary>
    /// Com o emissor desligado, só avisa. Ligado, reprova url pública inválida, chave de
    /// cifra ausente ou malformada, retenção menor que a duração do access token mais o
    /// intervalo da verificação de rotação, e idade mínima da limpeza menor que a duração
    /// do refresh token (change limpeza-tokens-openiddict, D3).
    /// </summary>
    public static void ValidateIdentityIssuerConfiguration(this IHost host)
    {
        var state = host.Services.GetRequiredService<IdentityIssuerState>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentityIssuerStartupValidation));

        if (!state.Enabled)
        {
            logger.LogWarning("Emissor OIDC desligado: PublicUrl:BaseUrl não configurada. O login e as demais rotas seguem iguais.");
            return;
        }

        if (state.Issuer is null)
        {
            throw new InvalidOperationException(
                "PublicUrl:BaseUrl inválido: o valor precisa ser uma URI absoluta http ou https, sem query nem fragmento " +
                "(por exemplo https://agentes.exemplo.com), ou ficar vazio para subir sem o emissor OIDC.");
        }

        IdentityKeyMaterialCipher.ParseKey(
            host.Services.GetRequiredService<IOptions<IdentityIssuerOptions>>().Value.SigningKeyEncryptionKey);

        var composed = ComposeWithoutKeys(host.Services);
        var accessTokenLifetime = composed.AccessTokenLifetime ?? TimeSpan.Zero;
        var checkInterval = host.Services.GetRequiredService<IdentityKeyRotationSchedule>().CheckInterval;
        if (IdentityKeyRotationPolicy.Retention < accessTokenLifetime + checkInterval)
        {
            throw new InvalidOperationException(
                $"Retenção das chaves do emissor ({IdentityKeyRotationPolicy.Retention}) menor que a duração do access token " +
                $"({accessTokenLifetime}) mais o intervalo da verificação de rotação ({checkInterval}): um token assinado " +
                "pela chave anterior deixaria de validar antes de expirar.");
        }

        // A linha de um refresh token resgatado precisa existir enquanto ele estiver no
        // prazo, ou a detecção de reuso para de valer em silêncio. Hoje passa (14 dias dos
        // dois lados); existe para quem mudar a duração do refresh token na #180.
        // Nulo, no OpenIddict, é refresh token sem prazo: nenhuma idade mínima cobre.
        var refreshTokenLifetime = composed.RefreshTokenLifetime;
        if (refreshTokenLifetime is null || IdentityTokenPrunePolicy.MinimumAge < refreshTokenLifetime)
        {
            throw new InvalidOperationException(
                $"Idade mínima da limpeza das entradas de token ({IdentityTokenPrunePolicy.MinimumAge}) menor que a duração " +
                $"do refresh token ({refreshTokenLifetime?.ToString() ?? "sem prazo"}): a linha de um refresh token resgatado sairia antes de ele vencer, " +
                "e o reuso deixaria de revogar a sessão. Suba IdentityTokenPrunePolicy.MinimumAge junto com a duração (#180).");
        }

        logger.LogInformation("Emissor OIDC ligado, issuer {Issuer}.", state.Issuer);
    }

    /// <summary>
    /// A nova forma da convenção 8, para endpoint que não é <c>RouteEndpoint</c> (F5).
    /// Aplica as configurações registradas na composição real, menos a das chaves, a uma
    /// instância nova de opções, e compara as URIs de todo tipo de endpoint, nos dois
    /// sentidos, com <see cref="IdentityIssuerEndpoints.Expected"/>. Reprova também URI
    /// que coincide com rota mapeada. Precisa rodar depois de todos os <c>Map*</c>.
    /// </summary>
    public static void ValidateIdentityIssuerEndpoints(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IdentityIssuerState>().Issuer is null)
        {
            return;
        }

        var options = ComposeWithoutKeys(app.Services);
        var configured = ConfiguredEndpoints(options).ToList();
        var configuredPaths = configured.Select(endpoint => endpoint.Path).ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();

        foreach (var (kind, path) in configured)
        {
            if (!IdentityIssuerEndpoints.Expected.ContainsKey(path))
            {
                problems.Add($"endpoint {kind} '{path}' configurado no servidor OIDC e fora da lista esperada");
            }
        }

        foreach (var expected in IdentityIssuerEndpoints.Expected.Keys)
        {
            if (!configuredPaths.Contains(expected))
            {
                problems.Add($"'{expected}' está na lista esperada e não corresponde a nenhum endpoint configurado no servidor OIDC");
            }
        }

        var mappedRoutes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? "").TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in configuredPaths.Where(mappedRoutes.Contains))
        {
            problems.Add($"'{path}' é endpoint do servidor OIDC e também rota mapeada do app");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Endpoints do emissor OIDC divergem da lista esperada: {string.Join("; ", problems)}.");
        }
    }

    private static OpenIddictServerOptions ComposeWithoutKeys(IServiceProvider services)
    {
        // Sem PostConfigure, e sem o configurer do anel: montar as opções de verdade
        // exigiria as chaves, e as chaves exigiriam o banco.
        var options = new OpenIddictServerOptions();
        foreach (var configurer in services.GetServices<IConfigureOptions<OpenIddictServerOptions>>()
                     .Where(configurer => configurer is not IdentityKeyRingOptionsConfigurer))
        {
            configurer.Configure(options);
        }

        return options;
    }

    private static IEnumerable<(string Kind, string Path)> ConfiguredEndpoints(OpenIddictServerOptions options)
    {
        var lists = new (string Kind, IEnumerable<Uri> Uris)[]
        {
            ("authorization", options.AuthorizationEndpointUris),
            ("configuration", options.ConfigurationEndpointUris),
            ("device authorization", options.DeviceAuthorizationEndpointUris),
            ("end session", options.EndSessionEndpointUris),
            ("end-user verification", options.EndUserVerificationEndpointUris),
            ("introspection", options.IntrospectionEndpointUris),
            ("jwks", options.JsonWebKeySetEndpointUris),
            ("pushed authorization", options.PushedAuthorizationEndpointUris),
            ("revocation", options.RevocationEndpointUris),
            ("token", options.TokenEndpointUris),
            ("userinfo", options.UserInfoEndpointUris),
            ("mtls device authorization", Alias(options.MtlsDeviceAuthorizationEndpointAliasUri)),
            ("mtls introspection", Alias(options.MtlsIntrospectionEndpointAliasUri)),
            ("mtls pushed authorization", Alias(options.MtlsPushedAuthorizationEndpointAliasUri)),
            ("mtls revocation", Alias(options.MtlsRevocationEndpointAliasUri)),
            ("mtls token", Alias(options.MtlsTokenEndpointAliasUri)),
            ("mtls userinfo", Alias(options.MtlsUserInfoEndpointAliasUri)),
        };

        return lists.SelectMany(list => list.Uris.Select(uri => (list.Kind, PathOf(uri))));

        static IEnumerable<Uri> Alias(Uri? uri) => uri is null ? [] : [uri];
    }

    private static string PathOf(Uri uri) =>
        uri.IsAbsoluteUri ? uri.AbsolutePath : "/" + uri.OriginalString.TrimStart('/');
}
