using System.Net.Http.Json;
using global::A2A;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AgentCardSecuritySchemeTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    // "Card inclui o esquema OAuth2 do Buteco" (spec a2a-agent-card, modificada pela change
    // aplicacoes-externas-credencial, D7). Até a #186 o card declarava o esquema HTTP "bearer", que não
    // dizia onde obter o token; agora declara o OAuth2 do emissor, sem token na consulta.
    [Fact]
    public async Task GetAgentCard_DeclaresTheButecoOAuth2ClientCredentialsScheme()
    {
        var agentId = await CreateAgentAsync();

        var response = await ServiceClientTokens.AnonymousClient(factory).GetAsync($"/agents/{agentId}/.well-known/agent-card.json");
        response.EnsureSuccessStatusCode();
        var card = await response.Content.ReadFromJsonAsync<AgentCard>();

        Assert.NotNull(card);
        var scheme = Assert.Single(card!.SecuritySchemes!);
        Assert.Equal("oauth2", scheme.Key);
        Assert.Null(scheme.Value.HttpAuthSecurityScheme);
        var oauth2 = scheme.Value.OAuth2SecurityScheme;
        Assert.NotNull(oauth2);
        Assert.Equal($"{ApiFactoryFixture.PublicBaseUrl}/connect/token", oauth2!.Flows.ClientCredentials!.TokenUrl);
        Assert.Equal(["buteco-api"], oauth2.Flows.ClientCredentials.Scopes.Keys);
        Assert.Equal($"{ApiFactoryFixture.PublicBaseUrl}/.well-known/openid-configuration", oauth2.OAuth2MetadataUrl);
    }

    // "Card referencia o esquema em SecurityRequirements" (a2a-agent-card): agora o oauth2, com o escopo.
    [Fact]
    public async Task GetAgentCard_IncludesSecurityRequirementReferencingTheOAuth2Scheme()
    {
        var agentId = await CreateAgentAsync();

        var card = await _client.GetFromJsonAsync<AgentCard>($"/agents/{agentId}/.well-known/agent-card.json");

        Assert.NotNull(card);
        var requirement = Assert.Single(card!.SecurityRequirements!);
        var scopes = Assert.Single(requirement.Schemes!, entry => entry.Key == "oauth2").Value;
        Assert.Equal(["buteco-api"], scopes.List);
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
