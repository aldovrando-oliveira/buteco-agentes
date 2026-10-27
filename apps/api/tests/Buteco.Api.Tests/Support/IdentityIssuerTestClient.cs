using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Cadastra o escopo e o cliente confidencial que os testes do emissor usam (design.md
/// da change emissor-oidc-api, D2). <b>Só aqui, depois do <c>MigrateAsync</c></b>: em
/// produção não há cliente nenhum, e o banco recém-migrado não tem linha de aplicação
/// (<c>IdentityIssuerMigrationTests</c>). O primeiro cliente real é da #186.
/// </summary>
public static class IdentityIssuerTestClient
{
    public const string ClientId = "cliente-teste";
    public const string ClientSecret = "segredo-do-cliente-teste";
    public const string Scope = "teste.api";
    public const string Resource = "buteco-teste";

    public static async Task EnsureAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        if (await scopes.FindByNameAsync(Scope) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = Scope,
                Resources = { Resource },
            });
        }

        if (await applications.FindByClientIdAsync(ClientId) is null)
        {
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                ClientType = ClientTypes.Confidential,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + Scope,
                },
            });
        }
    }

    /// <summary>
    /// Variante com refresh, só para a fixture da limpeza (design.md da change
    /// limpeza-tokens-openiddict, D7): com <c>gt:refresh_token</c>, o
    /// <c>client_credentials</c> com <c>offline_access</c> faz o OpenIddict gravar o que o
    /// login da #180 vai gravar, autorização ad hoc e refresh token ligados. Um
    /// <paramref name="clientId"/> por sessão dá um <c>subject</c> por sessão. Precisa do
    /// servidor com <c>AllowRefreshTokenFlow()</c>, que só o teste liga.
    /// </summary>
    public static async Task EnsureRefreshClientAsync(IServiceProvider services, string clientId)
    {
        await EnsureAsync(services);

        await using var scope = services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        if (await applications.FindByClientIdAsync(clientId) is null)
        {
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = ClientSecret,
                ClientType = ClientTypes.Confidential,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.Prefixes.Scope + Scope,
                },
            });
        }
    }

    public static FormUrlEncodedContent RefreshSessionRequest(string clientId) =>
        TokenRequest(clientId: clientId, scope: $"{Scope} {Scopes.OfflineAccess}");

    public static FormUrlEncodedContent RefreshRequest(string clientId, string refreshToken) =>
        new(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.RefreshToken,
            [Parameters.ClientId] = clientId,
            [Parameters.ClientSecret] = ClientSecret,
            [Parameters.RefreshToken] = refreshToken,
        });

    public static FormUrlEncodedContent TokenRequest(
        string grantType = GrantTypes.ClientCredentials,
        string clientId = ClientId,
        string clientSecret = ClientSecret,
        string? scope = Scope) =>
        new(new Dictionary<string, string>
        {
            [Parameters.GrantType] = grantType,
            [Parameters.ClientId] = clientId,
            [Parameters.ClientSecret] = clientSecret,
        }.Concat(scope is null ? [] : [KeyValuePair.Create(Parameters.Scope, scope)]));
}
