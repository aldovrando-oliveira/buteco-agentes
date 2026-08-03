using System.Net;
using System.Net.Http.Json;
using Buteco.Api.AgentMcpBindings.Requests;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

/// <summary>
/// Cobre a validação ao vivo de `allowedTools` em
/// `PUT /agents/{id}/mcp-servers` (Decision 2 do design.md da change
/// backend-mcp-selecao-tools) — precisa de <see cref="McpConnectionTestFixture"/>
/// (handshake MCP simulado) porque toda `allowedTools` não vazia exige um
/// `tools/list` real contra o servidor correspondente.
/// </summary>
public class AgentMcpBindingToolValidationTests(McpConnectionTestFixture factory) : IClassFixture<McpConnectionTestFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ReplaceAgentMcpServers_ValidAllowedTools_IsAccepted()
    {
        ResetHandler();
        factory.McpServerHandler.AvailableTools = [new FakeMcpTool("read_file"), new FakeMcpTool("write_file")];

        var agent = await CreateAgentAsync("Agente Tools A");
        var mcpServer = await CreateMcpServerAsync("MCP Tools A");

        var response = await PutMcpServersAsync(agent.Id, [(mcpServer.Id, new[] { "read_file" })]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.McpServers);
        Assert.Equal(["read_file"], updated.McpServers[0].AllowedTools);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_ToolNotOfferedByServer_ReturnsValidationProblemAndKeepsExistingLinks()
    {
        ResetHandler();
        factory.McpServerHandler.AvailableTools = [new FakeMcpTool("read_file")];

        var agent = await CreateAgentAsync("Agente Tools B");
        var mcpServer = await CreateMcpServerAsync("MCP Tools B");
        await PutMcpServersAsync(agent.Id, [(mcpServer.Id, new[] { "read_file" })]);

        var response = await PutMcpServersAsync(agent.Id, [(mcpServer.Id, new[] { "delete_everything" })]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{agent.Id}");
        var current = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(current!.McpServers);
        Assert.Equal(["read_file"], current.McpServers[0].AllowedTools);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_HandshakeFailsForOneOfMultipleServers_RejectsWholeOperation()
    {
        ResetHandler();
        factory.McpServerHandler.AvailableTools = [new FakeMcpTool("read_file")];

        var agent = await CreateAgentAsync("Agente Tools C");
        var reachableServer = await CreateMcpServerAsync("MCP Tools C1", "https://mcp-c1.example.com");
        var unreachableServer = await CreateMcpServerAsync("MCP Tools C2", "https://mcp-c2.example.com");

        factory.McpServerHandler.UnreachableUrls.Add("https://mcp-c2.example.com");

        var response = await PutMcpServersAsync(
            agent.Id,
            [(reachableServer.Id, new[] { "read_file" }), (unreachableServer.Id, new[] { "read_file" })]);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        // Nenhum vínculo aplicado — nem o do servidor cujo handshake teve
        // sucesso (Decision 2/6 do design.md: rejeição sempre atômica).
        var getResponse = await _client.GetAsync($"/agents/{agent.Id}");
        var current = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Empty(current!.McpServers);
    }

    [Fact]
    public async Task SameMcpServer_LinkedToTwoAgents_WithDifferentAllowedTools_NoInterference()
    {
        ResetHandler();
        factory.McpServerHandler.AvailableTools = [new FakeMcpTool("read_file"), new FakeMcpTool("write_file")];

        var agentOne = await CreateAgentAsync("Agente Tools D1");
        var agentTwo = await CreateAgentAsync("Agente Tools D2");
        var mcpServer = await CreateMcpServerAsync("MCP Tools D Compartilhado");

        await PutMcpServersAsync(agentOne.Id, [(mcpServer.Id, new[] { "read_file" })]);
        await PutMcpServersAsync(agentTwo.Id, [(mcpServer.Id, new[] { "read_file", "write_file" })]);

        var agentOneResponse = await _client.GetAsync($"/agents/{agentOne.Id}");
        var agentOneUpdated = await agentOneResponse.Content.ReadFromJsonAsync<AgentResponse>();
        var agentTwoResponse = await _client.GetAsync($"/agents/{agentTwo.Id}");
        var agentTwoUpdated = await agentTwoResponse.Content.ReadFromJsonAsync<AgentResponse>();

        Assert.Equal(["read_file"], agentOneUpdated!.McpServers[0].AllowedTools);
        Assert.Equal(["read_file", "write_file"], agentTwoUpdated!.McpServers[0].AllowedTools);
    }

    private void ResetHandler()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;
        factory.McpServerHandler.UnreachableUrls.Clear();
        factory.McpServerHandler.AvailableTools = [];
    }

    private Task<HttpResponseMessage> PutMcpServersAsync(Guid agentId, IReadOnlyList<(Guid McpServerId, string[] AllowedTools)> bindings) =>
        _client.PutAsJsonAsync(
            $"/agents/{agentId}/mcp-servers",
            new ReplaceAgentMcpServersRequest(bindings.Select(binding => new AgentMcpServerBindingRequest(binding.McpServerId, binding.AllowedTools)).ToList()));

    private async Task<AgentResponse> CreateAgentAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest(name, "Instruções.", Provider, Model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }

    private async Task<McpServerResponse> CreateMcpServerAsync(string name, string url = "https://mcp.example.com")
    {
        var response = await _client.PostAsJsonAsync("/mcp-servers", new CreateMcpServerRequest(name, "Descrição", url, "None", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<McpServerResponse>())!;
    }
}
