using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

public class McpConnectionTestEndpointsTests(McpConnectionTestFixture factory) : IClassFixture<McpConnectionTestFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task TestUnsavedConfig_HandshakeSuccessful_ReturnsSuccess()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;

        var request = new TestMcpServerConfigRequest("https://mcp.example.com", "None", null);
        var response = await _client.PostAsJsonAsync("/mcp-servers/test", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.True(result!.Success);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task TestUnsavedConfig_HostUnreachable_ReturnsFailureWithHostUnreachableReason()
    {
        factory.McpServerHandler.SimulateUnreachable = true;
        factory.McpServerHandler.RequiredBearerToken = null;

        var request = new TestMcpServerConfigRequest("https://unreachable.example.com", "None", null);
        var response = await _client.PostAsJsonAsync("/mcp-servers/test", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.False(result!.Success);
        Assert.Equal(McpConnectionTestFailureReason.HostUnreachable, result.FailureReason);

        factory.McpServerHandler.SimulateUnreachable = false;
    }

    [Fact]
    public async Task TestUnsavedConfig_CredentialRejected_ReturnsFailureWithCredentialRejectedReason()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = "expected-token";

        var request = new TestMcpServerConfigRequest("https://mcp.example.com", "BearerToken", "wrong-token");
        var response = await _client.PostAsJsonAsync("/mcp-servers/test", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.False(result!.Success);
        Assert.Equal(McpConnectionTestFailureReason.CredentialRejected, result.FailureReason);

        factory.McpServerHandler.RequiredBearerToken = null;
    }

    [Fact]
    public async Task TestUnsavedConfig_WithoutUrl_ReturnsValidationProblem()
    {
        var request = new TestMcpServerConfigRequest(null, "None", null);
        var response = await _client.PostAsJsonAsync("/mcp-servers/test", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TestUnsavedConfig_DoesNotPersistAnyMcpServer()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;

        using var scopeBefore = factory.Services.CreateScope();
        var countBefore = await scopeBefore.ServiceProvider.GetRequiredService<AppDbContext>().McpServers.CountAsync();

        var request = new TestMcpServerConfigRequest("https://mcp.example.com", "None", null);
        await _client.PostAsJsonAsync("/mcp-servers/test", request);

        using var scopeAfter = factory.Services.CreateScope();
        var countAfter = await scopeAfter.ServiceProvider.GetRequiredService<AppDbContext>().McpServers.CountAsync();

        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task TestSavedConfig_HandshakeSuccessful_ReturnsSuccess()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;

        var created = await CreateMcpServerAsync("Servidor Testável", "None", null);

        var response = await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.True(result!.Success);
    }

    [Fact]
    public async Task TestSavedConfig_HostUnreachable_ReturnsFailure()
    {
        factory.McpServerHandler.SimulateUnreachable = true;
        factory.McpServerHandler.RequiredBearerToken = null;

        var created = await CreateMcpServerAsync("Servidor Inalcançável", "None", null);

        var response = await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.False(result!.Success);
        Assert.Equal(McpConnectionTestFailureReason.HostUnreachable, result.FailureReason);

        factory.McpServerHandler.SimulateUnreachable = false;
    }

    [Fact]
    public async Task TestSavedConfig_CredentialRejected_ReturnsFailure()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = "correct-token";

        var created = await CreateMcpServerAsync("Servidor Com Token Errado", "BearerToken", "incorrect-token");

        var response = await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.False(result!.Success);
        Assert.Equal(McpConnectionTestFailureReason.CredentialRejected, result.FailureReason);

        factory.McpServerHandler.RequiredBearerToken = null;
    }

    [Fact]
    public async Task TestSavedConfig_Missing_ReturnsNotFound()
    {
        var response = await _client.PostAsync($"/mcp-servers/{Guid.NewGuid()}/test", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TestSavedConfig_InactiveServer_StillExecutesTest()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;

        var created = await CreateMcpServerAsync("Servidor Inativo Testável", "None", null);
        await _client.PostAsync($"/mcp-servers/{created.Id}/deactivate", content: null);

        var response = await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.True(result!.Success);
    }

    [Fact]
    public async Task TestSavedConfig_CredentialDecryptionFailure_ReturnsFailureWithoutNetworkAttempt()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;

        var created = await CreateMcpServerAsync("Servidor Com Credencial Corrompida", "BearerToken", "algum-token");

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Simula credencial que não decifra com a chave atualmente
            // configurada (ex.: chave rotacionada desde que foi salva) —
            // qualquer valor que falhe a verificação de tag do AES-GCM tem o
            // mesmo efeito observável de uma chave diferente.
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE mcp_servers SET "EncryptedCredential" = {"not-a-valid-ciphertext"} WHERE "Id" = {created.Id}""");
        }

        var requestCountBefore = factory.McpServerHandler.RequestCount;

        var response = await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.False(result!.Success);
        Assert.Equal(McpConnectionTestFailureReason.CredentialDecryptionFailed, result.FailureReason);
        Assert.Equal(requestCountBefore, factory.McpServerHandler.RequestCount);
    }

    [Fact]
    public async Task TestSavedConfig_DoesNotChangeMcpServerState()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;

        var created = await CreateMcpServerAsync("Servidor Estável", "None", null);

        await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null);

        var afterResponse = await _client.GetAsync($"/mcp-servers/{created.Id}");
        var after = await afterResponse.Content.ReadFromJsonAsync<McpServerResponse>();

        Assert.Equal(created.UpdatedAt, after!.UpdatedAt);
    }

    private async Task<McpServerResponse> CreateMcpServerAsync(string name, string authType, string? credential)
    {
        var request = new CreateMcpServerRequest(name, "Descrição", "https://mcp.example.com", authType, credential);
        var response = await _client.PostAsJsonAsync("/mcp-servers", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<McpServerResponse>())!;
    }
}
