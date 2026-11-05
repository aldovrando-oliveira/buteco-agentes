using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Auth;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O token do emissor de um cliente sem a audience do <c>apps/api</c> <b>não</b> é credencial
/// das rotas dele (spec <c>oidc-issuer</c>; design.md da change emissor-oidc-api, D9). O token
/// do painel aceito em <c>/agents</c> é provado pelo <see cref="IssuerTokenAcceptanceTests"/>,
/// e o 404 da rota de login pelo <see cref="GoneLoginTests"/>. O caso do token de serviço
/// antigo saiu com ele (change remover-hmac-token-servico); o par do emissor é o
/// <see cref="ServiceClientRouteTests"/>.
/// </summary>
public class IdentityIssuerCoexistenceTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => IdentityIssuerTestClient.EnsureAsync(factory.Services);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task IssuerJwt_OnAgents_IsUnauthorized()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        var tokenResponse = await client.PostAsync("/connect/token", IdentityIssuerTestClient.TokenRequest());
        var body = await tokenResponse.Content.ReadAsStringAsync();
        Assert.True(tokenResponse.IsSuccessStatusCode, body);
        var accessToken = JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/agents")).StatusCode);
    }
}
