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

    [Fact]
    public async Task PutAgentKnowledgeBases_WithoutToken_ReturnsUnauthorized()
    {
        var authenticated = factory.CreateClient();
        var agentId = await CreateAgentAsync(authenticated);

        var response = await UnauthenticatedClient().PutAsJsonAsync(
            $"/agents/{agentId}/knowledge-bases",
            new { knowledgeBaseIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Nada foi executado: o agente continua sem vínculo, e a asserção
        // negativa é o que separa "respondeu 401" de "respondeu 401 depois de
        // ter feito o trabalho".
        var agent = await authenticated.GetFromJsonAsync<System.Text.Json.JsonElement>($"/agents/{agentId}");
        Assert.Empty(agent.GetProperty("knowledgeBases").EnumerateArray());
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
