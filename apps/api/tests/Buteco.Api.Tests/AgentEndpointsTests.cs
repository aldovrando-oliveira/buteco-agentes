using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

public class AgentEndpointsTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateAgent_WithValidData_ReturnsCreatedAgent()
    {
        var request = new CreateAgentRequest("Atendente", "Você é um atendente simpático.", Provider, Model);

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.NotNull(agent);
        Assert.NotEqual(Guid.Empty, agent.Id);
        Assert.Equal(request.Name, agent.Name);
        Assert.Equal(request.Instructions, agent.Instructions);
        Assert.Equal(Provider, agent.Provider);
        Assert.Equal(Model, agent.Model);
    }

    [Fact]
    public async Task CreateAgent_WithoutNameOrInstructions_ReturnsValidationProblem()
    {
        var request = new CreateAgentRequest(null, null, Provider, Model);

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAgent_WithDescriptionAndSkills_ReturnsThemAsSent()
    {
        var skills = new List<SkillRequest> { new("Atendimento", "Responde dúvidas de clientes"), new("Vendas", null) };
        var request = new CreateAgentRequest("Atendente", "Você é um atendente simpático.", Provider, Model, "Agente de atendimento geral.", skills);

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal("Agente de atendimento geral.", agent!.Description);
        Assert.Equal(skills.Select(skill => skill.Name), agent.Skills.Select(skill => skill.Name));
        Assert.Equal(skills.Select(skill => skill.Description), agent.Skills.Select(skill => skill.Description));
    }

    [Fact]
    public async Task CreateAgent_WithoutDescriptionOrSkills_UsesDefaults()
    {
        var request = new CreateAgentRequest("Atendente", "Você é um atendente simpático.", Provider, Model);

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Null(agent!.Description);
        Assert.Empty(agent.Skills);
    }

    [Fact]
    public async Task CreateAgent_WithSkillWithoutName_ReturnsValidationProblem()
    {
        var skills = new List<SkillRequest> { new(null, "Descrição sem nome") };
        var request = new CreateAgentRequest("Atendente Skill Inválida", "Você é um atendente simpático.", Provider, Model, null, skills);

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var listResponse = await _client.GetAsync("/agents");
        var agents = await listResponse.Content.ReadFromJsonAsync<List<AgentResponse>>();
        Assert.DoesNotContain(agents!, agent => agent.Name == "Atendente Skill Inválida");
    }

    [Fact]
    public async Task UpdateAgent_ReplacesEntireSkillSet()
    {
        var skills = new List<SkillRequest> { new("Atendimento", null) };
        var createRequest = new CreateAgentRequest("Atendente", "Instruções originais.", Provider, Model, null, skills);
        var createResponse = await _client.PostAsJsonAsync("/agents", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<AgentResponse>();

        var newSkills = new List<SkillRequest> { new("Vendas", "Fecha negócios"), new("Suporte", null) };
        var updateRequest = new UpdateAgentRequest("Atendente", "Instruções originais.", Provider, Model, null, newSkills);
        var updateResponse = await _client.PutAsJsonAsync($"/agents/{created!.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal(newSkills.Select(skill => skill.Name), updated!.Skills.Select(skill => skill.Name));
        Assert.DoesNotContain(updated.Skills, skill => skill.Name == "Atendimento");
    }

    [Fact]
    public async Task UpdateAgent_WithSkillWithoutName_ReturnsValidationProblem()
    {
        var created = await CreateAgentAsync("Atendente", "Instruções originais.");

        var skills = new List<SkillRequest> { new("", "Descrição sem nome") };
        var request = new UpdateAgentRequest("Atendente", "Instruções originais.", Provider, Model, null, skills);
        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{created.Id}");
        var agent = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Empty(agent!.Skills);
    }

    [Fact]
    public async Task ListAgents_IncludesPreviouslyCreatedAgent()
    {
        var request = new CreateAgentRequest("Suporte", "Você resolve dúvidas de suporte.", Provider, Model);
        var createResponse = await _client.PostAsJsonAsync("/agents", request);
        var created = await createResponse.Content.ReadFromJsonAsync<AgentResponse>();

        var response = await _client.GetAsync("/agents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agents = await response.Content.ReadFromJsonAsync<List<AgentResponse>>();
        Assert.NotNull(agents);
        Assert.Contains(agents, a => a.Id == created!.Id);
    }

    [Fact]
    public async Task GetAgentById_Existing_ReturnsAgent()
    {
        var request = new CreateAgentRequest("Vendas", "Você ajuda com vendas.", Provider, Model);
        var createResponse = await _client.PostAsJsonAsync("/agents", request);
        var created = await createResponse.Content.ReadFromJsonAsync<AgentResponse>();

        var response = await _client.GetAsync($"/agents/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal(created.Id, agent!.Id);
        Assert.Equal(request.Name, agent.Name);
        Assert.Equal(Provider, agent.Provider);
        Assert.Equal(Model, agent.Model);
    }

    [Fact]
    public async Task GetAgentById_Missing_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/agents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAgentById_LegacyAgentWithoutProviderOrModel_ReturnsNullFields()
    {
        var legacyAgentId = await SeedLegacyAgentWithoutProviderOrModelAsync();

        var response = await _client.GetAsync($"/agents/{legacyAgentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Null(agent!.Provider);
        Assert.Null(agent.Model);
    }

    [Fact]
    public async Task ListAgents_IncludesLegacyAgentWithNullProviderAndModel()
    {
        var legacyAgentId = await SeedLegacyAgentWithoutProviderOrModelAsync();

        var response = await _client.GetAsync("/agents");

        var agents = await response.Content.ReadFromJsonAsync<List<AgentResponse>>();
        var legacy = agents!.Single(a => a.Id == legacyAgentId);
        Assert.Null(legacy.Provider);
        Assert.Null(legacy.Model);
    }

    [Fact]
    public async Task GetAgentById_LegacyAgentWithoutDescriptionOrSkills_ReturnsNullAndEmptyList()
    {
        var legacyAgentId = await SeedLegacyAgentWithoutDescriptionOrSkillsAsync();

        var response = await _client.GetAsync($"/agents/{legacyAgentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Null(agent!.Description);
        Assert.Empty(agent.Skills);
    }

    [Fact]
    public async Task ListAgents_IncludesLegacyAgentWithNullDescriptionAndEmptySkills()
    {
        var legacyAgentId = await SeedLegacyAgentWithoutDescriptionOrSkillsAsync();

        var response = await _client.GetAsync("/agents");

        var agents = await response.Content.ReadFromJsonAsync<List<AgentResponse>>();
        var legacy = agents!.Single(a => a.Id == legacyAgentId);
        Assert.Null(legacy.Description);
        Assert.Empty(legacy.Skills);
    }

    [Fact]
    public async Task UpdateAgent_WithValidData_ReturnsUpdatedAgent()
    {
        var created = await CreateAgentAsync("Atendente", "Instruções originais.");

        var request = new UpdateAgentRequest("Atendente Sênior", "Instruções atualizadas.", Provider, Model);
        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal(created.Id, agent!.Id);
        Assert.Equal(request.Name, agent.Name);
        Assert.Equal(request.Instructions, agent.Instructions);
        Assert.True(agent.IsActive);
    }

    [Fact]
    public async Task UpdateAgent_WithoutNameOrInstructions_ReturnsValidationProblem()
    {
        var created = await CreateAgentAsync("Atendente", "Instruções originais.");

        var request = new UpdateAgentRequest(null, null, null, null);
        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{created.Id}");
        var agent = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal("Atendente", agent!.Name);
    }

    [Fact]
    public async Task UpdateAgent_Missing_ReturnsNotFound()
    {
        var request = new UpdateAgentRequest("Nome", "Instruções", Provider, Model);

        var response = await _client.PutAsJsonAsync($"/agents/{Guid.NewGuid()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateThenActivate_ReflectsIsActive()
    {
        var created = await CreateAgentAsync("Atendente", "Instruções.");
        Assert.True(created.IsActive);

        var deactivateResponse = await _client.PostAsync($"/agents/{created.Id}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.False(deactivated!.IsActive);

        var activateResponse = await _client.PostAsync($"/agents/{created.Id}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.True(activated!.IsActive);
    }

    [Fact]
    public async Task DeactivateTwice_IsIdempotent()
    {
        var created = await CreateAgentAsync("Atendente", "Instruções.");

        var first = await _client.PostAsync($"/agents/{created.Id}/deactivate", content: null);
        var second = await _client.PostAsync($"/agents/{created.Id}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False((await second.Content.ReadFromJsonAsync<AgentResponse>())!.IsActive);
    }

    [Fact]
    public async Task ActivateTwice_IsIdempotent()
    {
        var created = await CreateAgentAsync("Atendente", "Instruções.");

        var first = await _client.PostAsync($"/agents/{created.Id}/activate", content: null);
        var second = await _client.PostAsync($"/agents/{created.Id}/activate", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True((await second.Content.ReadFromJsonAsync<AgentResponse>())!.IsActive);
    }

    [Fact]
    public async Task ActivateAndDeactivate_Missing_ReturnsNotFound()
    {
        var activateResponse = await _client.PostAsync($"/agents/{Guid.NewGuid()}/activate", content: null);
        var deactivateResponse = await _client.PostAsync($"/agents/{Guid.NewGuid()}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, activateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deactivateResponse.StatusCode);
    }

    [Fact]
    public async Task InactiveAgent_StillAppearsInListAndGetById()
    {
        var created = await CreateAgentAsync("Atendente", "Instruções.");
        await _client.PostAsync($"/agents/{created.Id}/deactivate", content: null);

        var listResponse = await _client.GetAsync("/agents");
        var agents = await listResponse.Content.ReadFromJsonAsync<List<AgentResponse>>();
        var listed = agents!.Single(a => a.Id == created.Id);
        Assert.False(listed.IsActive);

        var getResponse = await _client.GetAsync($"/agents/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.False(fetched!.IsActive);
    }

    private async Task<AgentResponse> CreateAgentAsync(string name, string instructions)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest(name, instructions, Provider, Model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }

    private async Task<Guid> SeedLegacyAgentWithoutProviderOrModelAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Simula um agente cadastrado antes desta capacidade existir — mesmo
        // estado produzido pela migration AddAgentProviderModel para linhas
        // já existentes (ver AgentUpdateProviderValidationTests para o mesmo padrão).
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {"Legado"}, {"Instruções legadas."}, {true}, {now}, {now})
             """);

        return agentId;
    }

    private async Task<Guid> SeedLegacyAgentWithoutDescriptionOrSkillsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Simula um agente cadastrado antes desta capacidade existir — mesmo
        // estado produzido pela migration AddAgentDescriptionAndSkills para
        // linhas já existentes (mesma técnica de
        // SeedLegacyAgentWithoutProviderOrModelAsync): não referencia
        // "Description"/"skills" no INSERT, deixando o DEFAULT da coluna
        // preencher. Passa pelo caminho real de serialização
        // (HasConversion/ValueComparer em AppDbContext) via GET de verdade —
        // diferente de AgentDescriptionAndSkillsMigrationTests, que cobre só
        // o schema.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {"Legado Sem Description"}, {"Instruções legadas."}, {true}, {Provider}, {Model}, {now}, {now})
             """);

        return agentId;
    }
}
