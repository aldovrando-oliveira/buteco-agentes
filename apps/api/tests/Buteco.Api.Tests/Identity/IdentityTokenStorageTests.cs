using System.Net;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Um pedido de <c>client_credentials</c> não grava nada (spec <c>issuer-token-storage</c>;
/// design.md da change limpeza-tokens-openiddict, D1), e o token continua verificável
/// pelo JWKS.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com o registro do
/// <c>ClientCredentialsAccessTokenStorageHandler</c> (hoje <c>TokenEntryStorageHandler</c>) comentado no
/// <c>AddIdentityIssuer</c>, a contagem de tokens vira 1 e o teste reprova (registro no
/// tasks.md, 2.3).
/// </para>
/// </summary>
public class IdentityTokenStorageTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => IdentityIssuerTestClient.EnsureAsync(factory.Services);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ClientCredentials_WritesNoTokenAndNoAuthorization_AndTheTokenStillVerifies()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsync("/connect/token", IdentityIssuerTestClient.TokenRequest());
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await dbContext.Set<OpenIddictEntityFrameworkCoreToken>().CountAsync());
            Assert.Equal(0, await dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>().CountAsync());
        }

        var accessToken = JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString()!;
        var jwks = new JsonWebKeySet(await client.GetStringAsync("/.well-known/jwks"));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuer = ApiFactoryFixture.PublicBaseUrl + "/",
            ValidAudience = IdentityIssuerTestClient.Resource,
            IssuerSigningKeys = jwks.GetSigningKeys(),
        });
        Assert.True(result.IsValid, result.Exception?.ToString());

        // Sem linha, o token não carrega o id dela (F5).
        Assert.False(new JsonWebToken(accessToken).TryGetPayloadValue<string>(Claims.Private.TokenId, out _));
    }
}

/// <summary>
/// O fluxo do usuário grava só código e refresh (spec <c>issuer-token-storage</c>, "Fluxo do
/// usuário grava só código e refresh" e "Renovação grava só o refresh novo"; design.md da
/// change usuarios-locais-login-emissor, D3.8).
///
/// <para>
/// Verificado contra o defeito (convenção 15): com o registro do
/// <c>TokenEntryStorageHandler</c> comentado no <c>AddIdentityIssuer</c>, as contagens de
/// access e id token deixam de ser zero e o teste reprova (registro no tasks.md, 6.7).
/// </para>
/// </summary>
public class IdentityUserTokenStorageTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private async Task<Dictionary<string, int>> CountByTypeAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<OpenIddictEntityFrameworkCoreToken>()
            .Where(token => token.Subject == subject)
            .GroupBy(token => token.Type!)
            .ToDictionaryAsync(group => group.Key, group => group.Count());
    }

    [Fact]
    public async Task UserFlow_WritesOnlyTheCodeAndTheRefresh_AndRenewalWritesOnlyTheNewRefresh()
    {
        var email = LocalUserSeed.UniqueEmail("armazenamento");
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, email, "senha-do-teste");
        var client = IdentityIssuerUserFlow.Client(factory);

        var tokens = await IdentityIssuerUserFlow.SignInAsync(client, factory.Services, email, "senha-do-teste");
        var subject = user.Id.ToString();

        var afterLogin = await CountByTypeAsync(subject);
        Assert.Equal(1, afterLogin.GetValueOrDefault(TokenTypeIdentifiers.Private.AuthorizationCode));
        Assert.Equal(1, afterLogin.GetValueOrDefault(TokenTypeIdentifiers.RefreshToken));
        Assert.Equal(0, afterLogin.GetValueOrDefault(TokenTypeIdentifiers.AccessToken));
        Assert.Equal(0, afterLogin.GetValueOrDefault(TokenTypeIdentifiers.IdentityToken));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var authorizations = await db.Set<OpenIddictEntityFrameworkCoreAuthorization>().Where(a => a.Subject == subject).ToListAsync();
            Assert.Equal(Statuses.Valid, Assert.Single(authorizations).Status);
        }

        await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(client, tokens.RefreshToken));

        var afterRefresh = await CountByTypeAsync(subject);
        Assert.Equal(2, afterRefresh.GetValueOrDefault(TokenTypeIdentifiers.RefreshToken));
        Assert.Equal(0, afterRefresh.GetValueOrDefault(TokenTypeIdentifiers.AccessToken));
        Assert.Equal(0, afterRefresh.GetValueOrDefault(TokenTypeIdentifiers.IdentityToken));
    }
}
