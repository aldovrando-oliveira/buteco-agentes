using System.Net;
using System.Net.Http.Json;
using Buteco.Api.AgentMcpBindings.Requests;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AgentMcpBindingEndpointsTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ReplaceAgentMcpServers_InitialSet_LinksServers()
    {
        var agent = await CreateAgentAsync("Agente A");
        var mcpServer = await CreateMcpServerAsync("MCP A");

        var response = await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.McpServers);
        Assert.Equal(mcpServer.Id, updated.McpServers[0].Id);
        Assert.Equal(mcpServer.Name, updated.McpServers[0].Name);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_DifferentSet_ReplacesLinks()
    {
        var agent = await CreateAgentAsync("Agente B");
        var mcpServerOne = await CreateMcpServerAsync("MCP B1");
        var mcpServerTwo = await CreateMcpServerAsync("MCP B2");

        await PutMcpServersAsync(agent.Id, [mcpServerOne.Id]);
        var response = await PutMcpServersAsync(agent.Id, [mcpServerTwo.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.McpServers);
        Assert.Equal(mcpServerTwo.Id, updated.McpServers[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_EmptyList_RemovesAllLinks()
    {
        var agent = await CreateAgentAsync("Agente C");
        var mcpServer = await CreateMcpServerAsync("MCP C");

        await PutMcpServersAsync(agent.Id, [mcpServer.Id]);
        var response = await PutMcpServersAsync(agent.Id, []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Empty(updated!.McpServers);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_SameSetTwice_IsIdempotent()
    {
        var agent = await CreateAgentAsync("Agente D");
        var mcpServer = await CreateMcpServerAsync("MCP D");

        var first = await PutMcpServersAsync(agent.Id, [mcpServer.Id]);
        var second = await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var updated = await second.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.McpServers);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_InvalidMcpServerId_ReturnsValidationProblemAndKeepsExistingLinks()
    {
        var agent = await CreateAgentAsync("Agente E");
        var mcpServer = await CreateMcpServerAsync("MCP E");
        await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        var response = await PutMcpServersAsync(agent.Id, [Guid.NewGuid()]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{agent.Id}");
        var current = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(current!.McpServers);
        Assert.Equal(mcpServer.Id, current.McpServers[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_MissingAgent_ReturnsNotFound()
    {
        var response = await PutMcpServersAsync(Guid.NewGuid(), []);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_InactiveMcpServer_IsAccepted()
    {
        var agent = await CreateAgentAsync("Agente F");
        var mcpServer = await CreateMcpServerAsync("MCP F");
        await _client.PostAsync($"/mcp-servers/{mcpServer.Id}/deactivate", content: null);

        var response = await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.McpServers);
    }

    [Fact]
    public async Task SameMcpServer_LinkedToTwoAgents_NoInterference()
    {
        var agentOne = await CreateAgentAsync("Agente G1");
        var agentTwo = await CreateAgentAsync("Agente G2");
        var mcpServer = await CreateMcpServerAsync("MCP G Compartilhado");

        await PutMcpServersAsync(agentOne.Id, [mcpServer.Id]);
        await PutMcpServersAsync(agentTwo.Id, [mcpServer.Id]);

        var agentOneResponse = await _client.GetAsync($"/agents/{agentOne.Id}");
        var agentOneUpdated = await agentOneResponse.Content.ReadFromJsonAsync<AgentResponse>();
        var agentTwoResponse = await _client.GetAsync($"/agents/{agentTwo.Id}");
        var agentTwoUpdated = await agentTwoResponse.Content.ReadFromJsonAsync<AgentResponse>();

        Assert.Single(agentOneUpdated!.McpServers);
        Assert.Single(agentTwoUpdated!.McpServers);
        Assert.Equal(mcpServer.Id, agentOneUpdated.McpServers[0].Id);
        Assert.Equal(mcpServer.Id, agentTwoUpdated.McpServers[0].Id);
    }

    [Fact]
    public async Task SameAgent_LinkedToTwoMcpServers()
    {
        var agent = await CreateAgentAsync("Agente H");
        var mcpServerOne = await CreateMcpServerAsync("MCP H1");
        var mcpServerTwo = await CreateMcpServerAsync("MCP H2");

        var response = await PutMcpServersAsync(agent.Id, [mcpServerOne.Id, mcpServerTwo.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal(2, updated!.McpServers.Count);
        Assert.Contains(updated.McpServers, m => m.Id == mcpServerOne.Id);
        Assert.Contains(updated.McpServers, m => m.Id == mcpServerTwo.Id);
    }

    [Fact]
    public async Task GetAgentByIdAndListAgents_ReflectLinkedMcpServers()
    {
        var agent = await CreateAgentAsync("Agente I");
        var mcpServer = await CreateMcpServerAsync("MCP I");
        await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        var getResponse = await _client.GetAsync($"/agents/{agent.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(fetched!.McpServers);
        Assert.Equal(mcpServer.Id, fetched.McpServers[0].Id);

        var listResponse = await _client.GetAsync("/agents");
        var agents = await listResponse.Content.ReadFromJsonAsync<List<AgentResponse>>();
        var listed = agents!.Single(a => a.Id == agent.Id);
        Assert.Single(listed.McpServers);
        Assert.Equal(mcpServer.Id, listed.McpServers[0].Id);
    }

    [Fact]
    public async Task AgentWithoutBinding_ReturnsEmptyMcpServersList()
    {
        var agent = await CreateAgentAsync("Agente J");

        var getResponse = await _client.GetAsync($"/agents/{agent.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.NotNull(fetched!.McpServers);
        Assert.Empty(fetched.McpServers);

        var listResponse = await _client.GetAsync("/agents");
        var agents = await listResponse.Content.ReadFromJsonAsync<List<AgentResponse>>();
        var listed = agents!.Single(a => a.Id == agent.Id);
        Assert.NotNull(listed.McpServers);
        Assert.Empty(listed.McpServers);
    }

    private Task<HttpResponseMessage> PutMcpServersAsync(Guid agentId, IReadOnlyList<Guid> mcpServerIds) =>
        _client.PutAsJsonAsync($"/agents/{agentId}/mcp-servers", new ReplaceAgentMcpServersRequest(mcpServerIds));

    private async Task<AgentResponse> CreateAgentAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest(name, "Instruções.", Provider, Model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }

    private async Task<McpServerResponse> CreateMcpServerAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/mcp-servers", new CreateMcpServerRequest(name, "Descrição", "https://mcp.example.com", "None", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<McpServerResponse>())!;
    }
}
