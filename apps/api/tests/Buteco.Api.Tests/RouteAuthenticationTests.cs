using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Auth.Requests;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class RouteAuthenticationTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    [Fact]
    public async Task GetAgents_WithoutToken_ReturnsUnauthorized()
    {
        var client = UnauthenticatedClient();

        var response = await client.GetAsync("/agents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAgents_WithInvalidToken_ReturnsUnauthorized()
    {
        var client = UnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "token-invalido");

        var response = await client.GetAsync("/agents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentCard_WithoutToken_StaysAnonymous()
    {
        var agentId = await CreateAgentAsync(factory.CreateClient());

        var response = await UnauthenticatedClient().GetAsync($"/agents/{agentId}/.well-known/agent-card.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_WithoutToken_StaysAnonymous()
    {
        var client = UnauthenticatedClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithoutToken_StaysAnonymous()
    {
        var client = UnauthenticatedClient();
        var request = new LoginRequest(ApiFactoryFixture.KnownOperatorUsername, ApiFactoryFixture.KnownOperatorPassword);

        var response = await client.PostAsJsonAsync("/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient UnauthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    private static async Task<Guid> CreateAgentAsync(HttpClient authenticatedClient)
    {
        var response = await authenticatedClient.PostAsJsonAsync(
            "/agents",
            new { name = "Agente de teste", instructions = "Instrução.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
