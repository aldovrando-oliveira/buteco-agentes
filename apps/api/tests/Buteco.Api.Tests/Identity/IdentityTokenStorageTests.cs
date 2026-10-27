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
/// <c>ClientCredentialsAccessTokenStorageHandler</c> comentado no
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
