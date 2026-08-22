using System.Net.Http.Json;
using global::A2A;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AgentCardSecuritySchemeTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetAgentCard_IncludesBearerHttpAuthSecurityScheme()
    {
        var agentId = await CreateAgentAsync();

        var card = await _client.GetFromJsonAsync<AgentCard>($"/agents/{agentId}/.well-known/agent-card.json");

        Assert.NotNull(card);
        Assert.True(card!.SecuritySchemes.TryGetValue("bearer", out var scheme));
        Assert.NotNull(scheme!.HttpAuthSecurityScheme);
        Assert.Equal("Bearer", scheme.HttpAuthSecurityScheme!.Scheme);
    }

    [Fact]
    public async Task GetAgentCard_IncludesSecurityRequirementReferencingBearerScheme()
    {
        var agentId = await CreateAgentAsync();

        var card = await _client.GetFromJsonAsync<AgentCard>($"/agents/{agentId}/.well-known/agent-card.json");

        Assert.NotNull(card);
        Assert.Contains(card!.SecurityRequirements, requirement => requirement.Schemes.ContainsKey("bearer"));
    }

    private async Task<Guid> CreateAgentAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/agents",
            new { name = "Agente card", instructions = "Instrução.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
