using System.Net;
using System.Net.Http.Json;
using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class McpServerToolsEndpointTests(McpConnectionTestFixture factory) : IClassFixture<McpConnectionTestFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ListTools_HandshakeSuccessful_ReturnsToolsFromServer()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;
        factory.McpServerHandler.AvailableTools =
        [
            new FakeMcpTool("read_file", "Lê um arquivo"),
            new FakeMcpTool("write_file", "Escreve em um arquivo"),
        ];

        var created = await CreateMcpServerAsync("Servidor Com Tools", "None", null);

        var response = await _client.GetAsync($"/mcp-servers/{created.Id}/tools");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpServerToolsResponse>();
        Assert.True(result!.Success);
        Assert.NotNull(result.Tools);
        Assert.Equal(2, result.Tools!.Count);
        Assert.Contains(result.Tools, tool => tool.Name == "read_file");
        Assert.Contains(result.Tools, tool => tool.Name == "write_file");

        factory.McpServerHandler.AvailableTools = [];
    }

    [Fact]
    public async Task ListTools_MissingMcpServer_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/mcp-servers/{Guid.NewGuid()}/tools");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListTools_HostUnreachable_ReturnsFailureResultWithoutTools()
    {
        factory.McpServerHandler.SimulateUnreachable = true;
        factory.McpServerHandler.RequiredBearerToken = null;

        var created = await CreateMcpServerAsync("Servidor Inalcançável Para Tools", "None", null);

        var response = await _client.GetAsync($"/mcp-servers/{created.Id}/tools");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpServerToolsResponse>();
        Assert.False(result!.Success);
        Assert.Null(result.Tools);
        Assert.Equal(McpConnectionTestFailureReason.HostUnreachable, result.FailureReason);

        factory.McpServerHandler.SimulateUnreachable = false;
    }

    [Fact]
    public async Task ListTools_CredentialRejected_ReturnsFailureResultWithoutTools()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = "correct-token";

        var created = await CreateMcpServerAsync("Servidor Com Token Errado Para Tools", "BearerToken", "incorrect-token");

        var response = await _client.GetAsync($"/mcp-servers/{created.Id}/tools");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpServerToolsResponse>();
        Assert.False(result!.Success);
        Assert.Null(result.Tools);
        Assert.Equal(McpConnectionTestFailureReason.CredentialRejected, result.FailureReason);

        factory.McpServerHandler.RequiredBearerToken = null;
    }

    [Fact]
    public async Task ListTools_InactiveMcpServer_StillExecutesDiscovery()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;
        factory.McpServerHandler.AvailableTools = [new FakeMcpTool("ping", "Verifica disponibilidade")];

        var created = await CreateMcpServerAsync("Servidor Inativo Com Tools", "None", null);
        await _client.PostAsync($"/mcp-servers/{created.Id}/deactivate", content: null);

        var response = await _client.GetAsync($"/mcp-servers/{created.Id}/tools");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpServerToolsResponse>();
        Assert.True(result!.Success);
        Assert.Single(result.Tools!);

        factory.McpServerHandler.AvailableTools = [];
    }

    private async Task<McpServerResponse> CreateMcpServerAsync(string name, string authType, string? credential)
    {
        var request = new CreateMcpServerRequest(name, "Descrição", "https://mcp.example.com", authType, credential);
        var response = await _client.PostAsJsonAsync("/mcp-servers", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<McpServerResponse>())!;
    }
}
