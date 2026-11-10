using Buteco.Api.Auth;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Identity.Storage;
using Buteco.Api.McpServers.Security;
using Buteco.Api.Options;
using Buteco.Api.Users.TwoFactor;
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
    /// cifra ausente ou malformada, segredo de cliente de serviço ausente, curto ou igual a
    /// outro segredo ou chave (change clientes-servico-emissor, D2), retenção menor que a maior duração (access ou refresh) mais o
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

        var identityOptions = host.Services.GetRequiredService<IOptions<IdentityIssuerOptions>>().Value;
        var signingKey = IdentityKeyMaterialCipher.ParseKey(identityOptions.SigningKeyEncryptionKey);

        // Chave por domínio (convenção 3): a do TOTP é obrigatória e distinta das outras duas,
        // mesmo com o cadastro fechado (verificacao-2-etapas-totp, D5). Sem banco.
        var twoFactorKey = TwoFactorSecretCipher.ParseKey(identityOptions.TwoFactorEncryptionKey);
        if (twoFactorKey.AsSpan().SequenceEqual(signingKey))
        {
            throw new InvalidOperationException(DistinctKeysMessage("Identity:SigningKeyEncryptionKey"));
        }

        var mcpKey = TryDecodeBase64(host.Services.GetRequiredService<IOptions<McpCryptoOptions>>().Value.CredentialEncryptionKey);
        if (mcpKey is not null && twoFactorKey.AsSpan().SequenceEqual(mcpKey))
        {
            throw new InvalidOperationException(DistinctKeysMessage("Mcp:CredentialEncryptionKey"));
        }

        ValidateServiceClientSecrets(identityOptions.ServiceClients, signingKey, twoFactorKey, mcpKey);

        // A retenção cobre o token mais longo que o emissor valida com as próprias chaves: o
        // refresh também é validado por elas (usuarios-locais-login-emissor, D3.7), e o access token
        // da aplicação externa tem duração própria (aplicacoes-externas-credencial, D5). Até a #186
        // eram só as durações globais; a da aplicação entra porque é uma constante do código, posta
        // no principal, e não um valor por aplicação no banco, que esta checagem não veria sem I/O.
        // Refresh sem prazo (nulo) não cabe em retenção nenhuma.
        var composed = ComposeWithoutKeys(host.Services);
        var accessTokenLifetime = composed.AccessTokenLifetime ?? TimeSpan.Zero;
        var checkInterval = host.Services.GetRequiredService<IdentityKeyRotationSchedule>().CheckInterval;
        if (composed.RefreshTokenLifetime is not { } refreshLifetime)
        {
            throw new InvalidOperationException(
                $"Refresh token sem prazo: nenhuma retenção das chaves do emissor ({IdentityKeyRotationPolicy.Retention}) o cobre.");
        }

        ValidateKeyRetention(
            IdentityKeyRotationPolicy.Retention, checkInterval, accessTokenLifetime, IdentityTokenLifetimes.ApplicationAccessToken, refreshLifetime);

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
    /// A maior das três durações que a retenção das chaves precisa cobrir, com o nome dela para a
    /// mensagem. Pública para o caso da 6.2 do tasks.md montar a checagem com a duração de aplicação
    /// acima da retenção, que a constante de produção não permite.
    /// </summary>
    public static (string Name, TimeSpan Lifetime) Longest(TimeSpan accessToken, TimeSpan applicationAccessToken, TimeSpan refreshToken) =>
        new[]
        {
            ("refresh token", refreshToken),
            ("access token", accessToken),
            ("access token de aplicação", applicationAccessToken),
        }.MaxBy(candidate => candidate.Item2);

    /// <summary>
    /// A conferência da retenção contra as três durações, separada para o mesmo caso. Lança com a
    /// mensagem do boot.
    /// </summary>
    public static void ValidateKeyRetention(TimeSpan retention, TimeSpan checkInterval, TimeSpan accessToken, TimeSpan applicationAccessToken, TimeSpan refreshToken)
    {
        var (longestName, longest) = Longest(accessToken, applicationAccessToken, refreshToken);
        if (retention < longest + checkInterval)
        {
            throw new InvalidOperationException(RetentionMessage(retention, longestName, longest, checkInterval));
        }
    }

    private static string RetentionMessage(TimeSpan retention, string longestName, TimeSpan longest, TimeSpan checkInterval) =>
        $"Retenção das chaves do emissor ({retention}) menor que a duração do {longestName} " +
        $"({longest}) mais o intervalo da verificação de rotação ({checkInterval}): um token assinado pela chave " +
        "anterior deixaria de validar antes de expirar.";

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

    /// <summary>
    /// Os segredos dos clientes de serviço (design.md da change clientes-servico-emissor, D2):
    /// presentes, em base64, com pelo menos 32 bytes, distintos entre si e das três chaves de
    /// cifra do <c>apps/api</c>, por bytes decodificados, no molde da chave das 2 etapas. A do
    /// MCP só entra quando está configurada e decodifica. Dois valores iguais desfariam a
    /// separação sem erro: um segredo, que vive também no contêiner do serviço, não pode ser
    /// a chave que decifra as chaves do emissor, os segredos do TOTP ou as credenciais de MCP.
    /// Obrigatórios mesmo sem <c>Connectors:BaseUrl</c>.
    /// </summary>
    private static void ValidateServiceClientSecrets(
        ServiceClientsOptions configured,
        byte[] signingKey,
        byte[] twoFactorKey,
        byte[]? mcpKey)
    {
        var secrets = new[]
        {
            (Name: ServiceClientSecret.InboxConfigurationName, Bytes: ServiceClientSecret.Decode(ServiceClientSecret.InboxConfigurationName, configured.Inbox.Secret)),
            (Name: ServiceClientSecret.ConnectorsConfigurationName, Bytes: ServiceClientSecret.Decode(ServiceClientSecret.ConnectorsConfigurationName, configured.Connectors.Secret)),
        };

        if (secrets[0].Bytes.AsSpan().SequenceEqual(secrets[1].Bytes))
        {
            throw new InvalidOperationException(SameValueMessage(secrets[0].Name, secrets[1].Name));
        }

        var keys = new List<(string Name, byte[] Bytes)>
        {
            ("Identity:SigningKeyEncryptionKey", signingKey),
            (TwoFactorSecretCipher.ConfigurationName, twoFactorKey),
        };
        if (mcpKey is not null)
        {
            keys.Add(("Mcp:CredentialEncryptionKey", mcpKey));
        }

        foreach (var secret in secrets)
        {
            foreach (var key in keys)
            {
                if (secret.Bytes.AsSpan().SequenceEqual(key.Bytes))
                {
                    throw new InvalidOperationException(SameValueMessage(secret.Name, key.Name));
                }
            }
        }
    }

    private static string SameValueMessage(string name, string other) =>
        $"{name} é igual a {other}: cada segredo de serviço precisa ser distinto do outro e das chaves de cifra. " +
        "Gere outro com `openssl rand -base64 32`.";

    private static string DistinctKeysMessage(string other) =>
        $"{TwoFactorSecretCipher.ConfigurationName} é igual a {other}: as chaves precisam ser distintas, uma por domínio. " +
        "Gere outra com `openssl rand -base64 32`.";

    private static byte[]? TryDecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
