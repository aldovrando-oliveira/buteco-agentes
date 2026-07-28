using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AgentEndpointsTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateAgent_WithValidData_ReturnsCreatedAgent()
    {
        var request = new CreateAgentRequest("Atendente", "Você é um atendente simpático.");

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.NotNull(agent);
        Assert.NotEqual(Guid.Empty, agent.Id);
        Assert.Equal(request.Name, agent.Name);
        Assert.Equal(request.Instructions, agent.Instructions);
    }

    [Fact]
    public async Task CreateAgent_WithoutNameOrInstructions_ReturnsValidationProblem()
    {
        var request = new CreateAgentRequest(null, null);

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListAgents_IncludesPreviouslyCreatedAgent()
    {
        var request = new CreateAgentRequest("Suporte", "Você resolve dúvidas de suporte.");
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
        var request = new CreateAgentRequest("Vendas", "Você ajuda com vendas.");
        var createResponse = await _client.PostAsJsonAsync("/agents", request);
        var created = await createResponse.Content.ReadFromJsonAsync<AgentResponse>();

        var response = await _client.GetAsync($"/agents/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal(created.Id, agent!.Id);
        Assert.Equal(request.Name, agent.Name);
    }

    [Fact]
    public async Task GetAgentById_Missing_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/agents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAgent_WithValidData_ReturnsUpdatedAgent()
    {
        var created = await CreateAgentAsync("Atendente", "Instruções originais.");

        var request = new UpdateAgentRequest("Atendente Sênior", "Instruções atualizadas.");
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

        var request = new UpdateAgentRequest(null, null);
        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{created.Id}");
        var agent = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal("Atendente", agent!.Name);
    }

    [Fact]
    public async Task UpdateAgent_Missing_ReturnsNotFound()
    {
        var request = new UpdateAgentRequest("Nome", "Instruções");

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
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest(name, instructions));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }
}
