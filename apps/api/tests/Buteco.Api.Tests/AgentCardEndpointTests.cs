using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

public class AgentCardEndpointTests(A2ATaskLifecycleFixture fixture) : IClassFixture<A2ATaskLifecycleFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task GetAgentCard_ForRecentlyCreatedAgentWithDescribedSkill_ReflectsNameDescriptionAndSkills()
    {
        var agentId = await CreateAgentAsync(
            name: "Atendente de Suporte",
            description: "Responde dúvidas de clientes.",
            skills: [new SkillRequest("Consulta CEP", "Consulta endereço a partir do CEP.")]);

        var card = await GetAgentCardAsync(agentId);

        Assert.Equal("Atendente de Suporte", card.GetProperty("name").GetString());
        Assert.Equal("Responde dúvidas de clientes.", card.GetProperty("description").GetString());

        var skill = Assert.Single(card.GetProperty("skills").EnumerateArray());
        Assert.Equal("Consulta CEP", skill.GetProperty("name").GetString());
        Assert.Equal("Consulta endereço a partir do CEP.", skill.GetProperty("description").GetString());
        Assert.False(string.IsNullOrEmpty(skill.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task GetAgentCard_ForSkillWithoutDescription_MapsDescriptionAsEmptyString()
    {
        var agentId = await CreateAgentAsync(skills: [new SkillRequest("Consulta CEP", null)]);

        var card = await GetAgentCardAsync(agentId);

        var skill = Assert.Single(card.GetProperty("skills").EnumerateArray());
        Assert.Equal(string.Empty, skill.GetProperty("description").GetString());
    }

    [Fact]
    public async Task GetAgentCard_ForCollidingSkillNames_ReturnsDistinctIdsInRealResponse()
    {
        var agentId = await CreateAgentAsync(skills:
        [
            new SkillRequest("Consulta CEP", null),
            new SkillRequest("Consulta CEP", "Segunda variante"),
            new SkillRequest("Consulta CEP", "Terceira variante"),
        ]);

        var card = await GetAgentCardAsync(agentId);

        var ids = card.GetProperty("skills").EnumerateArray().Select(skill => skill.GetProperty("id").GetString()).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    [Fact]
    public async Task GetAgentCard_ForUnknownAgent_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/agents/{Guid.NewGuid()}/.well-known/agent-card.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentCard_AfterUpdatingAgent_ReflectsNewValuesWithoutStaleData()
    {
        var agentId = await CreateAgentAsync(
            name: "Nome Original",
            description: "Descrição original.",
            skills: [new SkillRequest("Skill Original", "Descrição da skill original.")]);

        var originalCard = await GetAgentCardAsync(agentId);
        Assert.Equal("Nome Original", originalCard.GetProperty("name").GetString());

        var updateResponse = await _client.PutAsJsonAsync(
            $"/agents/{agentId}",
            new UpdateAgentRequest("Nome Atualizado", "Instruções atualizadas.", "openai", "gpt-5.6-sol", "Descrição atualizada.", [new SkillRequest("Skill Nova", "Descrição da skill nova.")]));
        updateResponse.EnsureSuccessStatusCode();

        var updatedCard = await GetAgentCardAsync(agentId);

        Assert.Equal("Nome Atualizado", updatedCard.GetProperty("name").GetString());
        Assert.Equal("Descrição atualizada.", updatedCard.GetProperty("description").GetString());
        var skill = Assert.Single(updatedCard.GetProperty("skills").EnumerateArray());
        Assert.Equal("Skill Nova", skill.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetAgentCard_ForDeactivatedAgent_StillReturnsOk()
    {
        var agentId = await CreateAgentAsync();

        var deactivateResponse = await _client.PostAsync($"/agents/{agentId}/deactivate", content: null);
        deactivateResponse.EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/agents/{agentId}/.well-known/agent-card.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentCard_ForAgentWithoutProviderOrModel_StillReturnsOk()
    {
        var agentId = await SeedLegacyAgentWithoutProviderOrModelAsync();

        var response = await _client.GetAsync($"/agents/{agentId}/.well-known/agent-card.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentCard_ForAgentWithoutSkills_ReturnsEmptySkillsList()
    {
        var agentId = await CreateAgentAsync();

        var card = await GetAgentCardAsync(agentId);

        Assert.Empty(card.GetProperty("skills").EnumerateArray());
    }

    [Fact]
    public async Task GetAgentCard_Shape_MatchesFixedProtocolFieldsAndIsStableAcrossCalls()
    {
        var agentId = await CreateAgentAsync(skills: [new SkillRequest("Consulta CEP", "Consulta endereço a partir do CEP.")]);

        var firstCard = await GetAgentCardAsync(agentId);
        var secondCard = await GetAgentCardAsync(agentId);

        Assert.Equal("1.0.0", firstCard.GetProperty("version").GetString());
        Assert.Equal(["text/plain"], firstCard.GetProperty("defaultInputModes").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(["text/plain"], firstCard.GetProperty("defaultOutputModes").EnumerateArray().Select(m => m.GetString()));

        var capabilities = firstCard.GetProperty("capabilities");
        Assert.False(capabilities.GetProperty("streaming").GetBoolean());
        Assert.False(capabilities.GetProperty("pushNotifications").GetBoolean());

        var supportedInterface = Assert.Single(firstCard.GetProperty("supportedInterfaces").EnumerateArray());
        Assert.Equal($"http://localhost:5017/agents/{agentId}/a2a", supportedInterface.GetProperty("url").GetString());

        var firstSkillId = firstCard.GetProperty("skills")[0].GetProperty("id").GetString();
        var secondSkillId = secondCard.GetProperty("skills")[0].GetProperty("id").GetString();
        Assert.Equal(firstSkillId, secondSkillId);
    }

    [Fact]
    public async Task GetExtendedAgentCard_ViaJsonRpc_RemainsUnsupported()
    {
        var agentId = await CreateAgentAsync();

        var payload = new { jsonrpc = "2.0", id = 1, method = "GetExtendedAgentCard", @params = new { } };
        var response = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.TryGetProperty("error", out var error));
        Assert.Equal(-32007, error.GetProperty("code").GetInt32());
    }

    private async Task<JsonElement> GetAgentCardAsync(Guid agentId)
    {
        var response = await _client.GetAsync($"/agents/{agentId}/.well-known/agent-card.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateAgentAsync(
        string name = "Atendente",
        string? description = null,
        IReadOnlyList<SkillRequest>? skills = null)
    {
        var response = await _client.PostAsJsonAsync(
            "/agents",
            new CreateAgentRequest(name, "Responda com simpatia.", "openai", "gpt-5.6-sol", description, skills));
        response.EnsureSuccessStatusCode();
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        return agent!.Id;
    }

    private async Task<Guid> SeedLegacyAgentWithoutProviderOrModelAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {"Legado"}, {"Instruções legadas."}, {true}, {now}, {now})
             """);

        return agentId;
    }
}
