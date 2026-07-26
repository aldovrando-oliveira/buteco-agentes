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
}
