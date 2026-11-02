using System.Net;
using System.Net.Http.Json;
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
    public async Task Session_WithoutToken_StaysAnonymous()
    {
        // O POST /auth/session é a entrada do login pelo emissor, e anônimo; o
        // /auth/login saiu na change painel-entra-pelo-emissor (D8). Sem e-mail nem
        // senha, a rota responde o 400 dela, e não o 401 de rota autenticada.
        var client = UnauthenticatedClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/session")
        {
            Content = JsonContent.Create(new { email = "", senha = "" }),
        };
        request.Headers.Add("Origin", LocalUserSeed.IssuerOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SecondFactor_WithoutToken_StaysAnonymous()
    {
        // O segundo passo do login é anônimo, como o primeiro (change
        // verificacao-2-etapas-totp, D2). Sem a sessão pendente, a rota responde o 401 dela,
        // com o título, e não o 401 vazio de rota autenticada.
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/session/second-factor")
        {
            Content = JsonContent.Create(new { codigo = "123456" }),
        };
        request.Headers.Add("Origin", LocalUserSeed.IssuerOrigin);

        var response = await UnauthenticatedClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("A entrada expirou", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET", "/account/two-factor")]
    [InlineData("POST", "/account/two-factor/setup")]
    [InlineData("POST", "/users/0199a000-0000-7000-8000-000000000000/two-factor/reset")]
    public async Task TwoFactorRoutesOfAPerson_WithoutToken_ReturnUnauthorized(string method, string path)
    {
        var response = await UnauthenticatedClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
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
