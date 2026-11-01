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
/// A fronteira entre esta change e a #180 (spec <c>oidc-issuer</c>; design.md da change
/// emissor-oidc-api, D9): com o emissor ligado, a validação HMAC segue igual, e o token
/// do emissor <b>não</b> é credencial das rotas do <c>apps/api</c>. O caso do login HMAC
/// saiu com o <c>POST /auth/login</c> (change painel-entra-pelo-emissor, D8): o HMAC de
/// operador aceito em <c>/agents</c> é provado por toda fixture, pelo
/// <see cref="TestAuthentication.AttachOperatorToken"/>, e o 404 da rota pelo
/// <see cref="GoneLoginTests"/>.
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

    [Fact]
    public async Task InboxServiceToken_StillReachesItsRoute()
    {
        var created = await factory.CreateClient().PostAsJsonAsync(
            "/agents",
            new { name = "Agente", instructions = "Instrução.", provider = "openai", model = "gpt-5.6-sol" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var agentId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        var client = factory.CreateClient();
        var (serviceToken, _) = factory.Services.GetRequiredService<ITokenService>()
            .Issue(ServiceScopeAuthorizationHandler.InboxSubject, TimeSpan.FromMinutes(5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/agents/{agentId}")).StatusCode);
    }
}
