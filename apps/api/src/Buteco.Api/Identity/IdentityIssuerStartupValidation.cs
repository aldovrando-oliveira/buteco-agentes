using Buteco.Api.Auth;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Identity.Storage;
using Buteco.Api.Options;
using Microsoft.AspNetCore.Authorization;
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
    /// Reprova url pública ausente ou inválida, chave de
    /// cifra ausente ou malformada, retenção menor que a maior duração (access ou refresh) mais o
    /// intervalo da verificação de rotação, e idade mínima da limpeza menor que a duração
    /// do refresh token (change limpeza-tokens-openiddict, D3).
    /// </summary>
    public static void ValidateIdentityIssuerConfiguration(this IHost host)
    {
        var state = host.Services.GetRequiredService<IdentityIssuerState>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentityIssuerStartupValidation));

        // Obrigatória desde a #180 (D3.4): sem issuer não há login de usuário.
        if (state.Issuer is null)
        {
            var configured = host.Services.GetRequiredService<IConfiguration>()[$"{PublicUrlOptions.SectionName}:{nameof(PublicUrlOptions.BaseUrl)}"];
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(configured)
                ? "PublicUrl:BaseUrl não configurada: a url pública é obrigatória, porque é o issuer do emissor OIDC e o login " +
                  "de usuário depende dele (por exemplo https://agentes.exemplo.com)."
                : "PublicUrl:BaseUrl inválido: o valor precisa ser uma URI absoluta http ou https, sem query nem fragmento " +
                  "(por exemplo https://agentes.exemplo.com).");
        }

        IdentityKeyMaterialCipher.ParseKey(
            host.Services.GetRequiredService<IOptions<IdentityIssuerOptions>>().Value.SigningKeyEncryptionKey);

        // A retenção cobre o token mais longo que o emissor valida com as próprias chaves: o
        // refresh também é validado por elas (usuarios-locais-login-emissor, D3.7). Só as
        // durações globais: uma duração por aplicação (#186) não aparece aqui. Refresh sem
        // prazo (nulo) não cabe em retenção nenhuma.
        var composed = ComposeWithoutKeys(host.Services);
        var accessTokenLifetime = composed.AccessTokenLifetime ?? TimeSpan.Zero;
        var checkInterval = host.Services.GetRequiredService<IdentityKeyRotationSchedule>().CheckInterval;
        if (composed.RefreshTokenLifetime is not { } refreshLifetime)
        {
            throw new InvalidOperationException(
                $"Refresh token sem prazo: nenhuma retenção das chaves do emissor ({IdentityKeyRotationPolicy.Retention}) o cobre.");
        }

        var (longestName, longest) = refreshLifetime >= accessTokenLifetime
            ? ("refresh token", refreshLifetime)
            : ("access token", accessTokenLifetime);
        if (IdentityKeyRotationPolicy.Retention < longest + checkInterval)
        {
            throw new InvalidOperationException(
                $"Retenção das chaves do emissor ({IdentityKeyRotationPolicy.Retention}) menor que a duração do {longestName} " +
                $"({longest}) mais o intervalo da verificação de rotação ({checkInterval}): um token assinado pela chave " +
                "anterior deixaria de validar antes de expirar.");
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
    /// sem passthrough que coincide com rota mapeada, e URI com passthrough sem rota
    /// mapeada anônima e de mesmo motivo (change usuarios-locais-login-emissor, D3.6).
    /// Precisa rodar depois de todos os <c>Map*</c>.
    /// </summary>
    public static void ValidateIdentityIssuerEndpoints(this WebApplication app)
    {
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
            .GroupBy(endpoint => "/" + (endpoint.RoutePattern.RawText ?? "").TrimStart('/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var path in configuredPaths)
        {
            var passthrough = IdentityIssuerEndpoints.Expected.TryGetValue(path, out var expected) && expected.Passthrough;
            var routes = mappedRoutes.GetValueOrDefault(path) ?? [];

            if (!passthrough && routes.Count > 0)
            {
                problems.Add($"'{path}' é endpoint do servidor OIDC e também rota mapeada do app");
            }
            else if (passthrough && !routes.Any(route => IsAnonymousWithReason(route, expected!.Reason)))
            {
                problems.Add(
                    $"'{path}' é endpoint em passthrough e não tem rota mapeada de mesmo caminho, anônima e classificada como {expected!.Reason}");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Endpoints do emissor OIDC divergem da lista esperada: {string.Join("; ", problems)}.");
        }
    }

    private static bool IsAnonymousWithReason(RouteEndpoint route, AnonymousRouteReason reason) =>
        route.Metadata.GetMetadata<IAllowAnonymous>() is not null &&
        route.Metadata.GetMetadata<AnonymousRouteClassification>()?.Reason == reason;

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
