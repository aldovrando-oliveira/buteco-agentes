using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buteco.Api.Auth;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

// Cobertura de ServiceScopeAuthorizationHandler (design.md, Decision 3):
// um token com sub "service:inbox" só é autorizado nas duas rotas que
// apps/inbox de fato consome.
public class ServiceScopeAuthorizationTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    [Fact]
    public async Task ServiceToken_OnCreateAgent_ReturnsForbidden()
    {
        var client = ServiceScopedClient();

        var response = await client.PostAsJsonAsync(
            "/agents",
            new { name = "Agente", instructions = "Instrução.", provider = "openai", model = "gpt-5.6-sol" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceToken_OnGetAgentById_IsAuthorized()
    {
        var agentId = await CreateAgentAsync(factory.CreateClient());

        var response = await ServiceScopedClient().GetAsync($"/agents/{agentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ServiceToken_OnA2ASendMessage_IsAuthorized()
    {
        var agentId = await CreateAgentAsync(factory.CreateClient());
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new
            {
                message = new
                {
                    role = "ROLE_USER",
                    parts = new[] { new { text = "Olá" } },
                    messageId = Guid.NewGuid().ToString(),
                },
            },
        };

        var response = await ServiceScopedClient().PostAsJsonAsync($"/agents/{agentId}/a2a", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ServiceToken_OnKnowledgeIndexDiagnostics_ReturnsForbidden()
    {
        var response = await ServiceScopedClient().GetAsync("/knowledge-index/diagnostics");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient ServiceScopedClient()
    {
        var client = factory.CreateClient();
        var tokenService = factory.Services.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.Issue(ServiceScopeAuthorizationHandler.ServiceSubject, TimeSpan.FromMinutes(5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
