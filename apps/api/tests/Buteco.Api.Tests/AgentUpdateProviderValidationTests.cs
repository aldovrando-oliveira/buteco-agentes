using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

public class AgentUpdateProviderValidationTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task UpdateAgent_WithoutProviderOrModel_ReturnsValidationProblem()
    {
        var created = await CreateAgentAsync();

        var request = new UpdateAgentRequest("Atendente", "Instruções.", null, null);
        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAgent_WithProviderNotConfigured_ReturnsValidationProblem()
    {
        var created = await CreateAgentAsync();

        var request = new UpdateAgentRequest("Atendente", "Instruções.", "gemini", "gemini-3.6-flash");
        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{created.Id}");
        var unchanged = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal(Provider, unchanged!.Provider);
    }

    [Fact]
    public async Task UpdateAgent_WithModelNotInProviderCatalog_ReturnsValidationProblem()
    {
        var created = await CreateAgentAsync();

        var request = new UpdateAgentRequest("Atendente", "Instruções.", "openai", "modelo-inexistente");
        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAgent_LegacyAgentWithoutProviderOrModel_LeavesReconfigurationStateWithValidUpdate()
    {
        var legacyAgentId = await SeedLegacyAgentWithoutProviderOrModelAsync();

        var getBefore = await _client.GetAsync($"/agents/{legacyAgentId}");
        var before = await getBefore.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Null(before!.Provider);
        Assert.Null(before.Model);

        var request = new UpdateAgentRequest("Legado Atualizado", "Instruções atualizadas.", Provider, Model);
        var response = await _client.PutAsJsonAsync($"/agents/{legacyAgentId}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal(Provider, updated!.Provider);
        Assert.Equal(Model, updated.Model);
    }

    private async Task<AgentResponse> CreateAgentAsync()
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest("Atendente", "Instruções.", Provider, Model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }

    private async Task<Guid> SeedLegacyAgentWithoutProviderOrModelAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Simula um agente cadastrado antes desta capacidade existir: inserção
        // direta via SQL, sem passar pelo construtor de Agent (que hoje exige
        // Provider/Model não-nulos) — Provider/Model ficam NULL, mesmo estado
        // produzido pela migration AddAgentProviderModel para linhas existentes.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {"Legado"}, {"Instruções legadas."}, {true}, {now}, {now})
             """);

        return agentId;
    }
}
