using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.McpServers.Security;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

public class McpServerEndpointsTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    // --- Ordenação (api-response-ordering) ---------------------------------

    [Fact]
    public async Task McpServersWithEqualCreatedAt_AreTieBrokenById()
    {
        var first = await CreateServerAsync("MCP Empate 1");
        var second = await CreateServerAsync("MCP Empate 2");
        var third = await CreateServerAsync("MCP Empate 3");

        var tied = new[] { first.Id, second.Id, third.Id };
        await CreatedAtTie.ForceAsync(factory.Services, "mcp_servers", tied);

        var response = await _client.GetAsync("/mcp-servers");
        response.EnsureSuccessStatusCode();
        var listed = (await response.Content.ReadFromJsonAsync<List<McpServerResponse>>())!;

        var observed = listed.Where(m => tied.Contains(m.Id)).Select(m => m.Id).ToList();
        Assert.Equal(tied.Order().ToList(), observed);
    }

    // Metade determinística (design.md, D6).
    [Fact]
    public async Task McpServerCatalogQuery_EmitsTieBreakAsLastOrderByTerm()
    {
        await CreateServerAsync("MCP SQL Ordem");

        var commands = await factory.SqlCapture.CaptureAsync(async () =>
        {
            (await _client.GetAsync("/mcp-servers")).EnsureSuccessStatusCode();
        });

        var query = EmittedSqlCapture.SingleCommandContaining(commands, "FROM mcp_servers", "ORDER BY");
        EmittedSqlCapture.AssertOrderByEndsWithTieBreak(query);
    }

    private async Task<McpServerResponse> CreateServerAsync(string name)
    {
        var response = await _client.PostAsJsonAsync(
            "/mcp-servers",
            new CreateMcpServerRequest(name, "Servidor de teste.", "https://mcp.exemplo.test/sse", "None", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<McpServerResponse>())!;
    }

    [Fact]
    public async Task CreateMcpServer_WithoutAuthentication_ReturnsCreatedServer()
    {
        var request = new CreateMcpServerRequest("Zendesk MCP", "Ferramentas de suporte", "https://mcp.example.com", "None", null);

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var mcpServer = await response.Content.ReadFromJsonAsync<McpServerResponse>();
        Assert.NotNull(mcpServer);
        Assert.NotEqual(Guid.Empty, mcpServer.Id);
        Assert.Equal(request.Name, mcpServer.Name);
        Assert.Equal(request.Description, mcpServer.Description);
        Assert.Equal(request.Url, mcpServer.Url);
        Assert.Equal(McpServerAuthType.None, mcpServer.AuthType);
        Assert.True(mcpServer.IsActive);
    }

    [Fact]
    public async Task CreateMcpServer_WithBearerToken_ReturnsCreatedServerWithoutCredentialInBody()
    {
        var request = new CreateMcpServerRequest("Linear MCP", "Gestão de issues", "https://mcp.linear.example.com", "BearerToken", "s3cr3t-token");

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("s3cr3t-token", body);
        Assert.DoesNotContain("redential", body, StringComparison.OrdinalIgnoreCase);

        var mcpServer = await response.Content.ReadFromJsonAsync<McpServerResponse>();
        Assert.Equal(McpServerAuthType.BearerToken, mcpServer!.AuthType);
    }

    [Fact]
    public async Task CreateMcpServer_WithoutNameOrUrl_ReturnsValidationProblem()
    {
        var request = new CreateMcpServerRequest(null, "Descrição", null, "None", null);

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMcpServer_WithBearerTokenAndNoCredential_ReturnsValidationProblem()
    {
        var request = new CreateMcpServerRequest("Servidor", "Descrição", "https://mcp.example.com", "BearerToken", null);

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListMcpServers_IncludesPreviouslyCreatedServer()
    {
        var created = await CreateMcpServerAsync("Servidor Listado", McpServerAuthType.None, null);

        var response = await _client.GetAsync("/mcp-servers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mcpServers = await response.Content.ReadFromJsonAsync<List<McpServerResponse>>();
        Assert.NotNull(mcpServers);
        Assert.Contains(mcpServers, m => m.Id == created.Id);
    }

    [Fact]
    public async Task GetMcpServerById_Existing_ReturnsServer()
    {
        var created = await CreateMcpServerAsync("Servidor Consultado", McpServerAuthType.None, null);

        var response = await _client.GetAsync($"/mcp-servers/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mcpServer = await response.Content.ReadFromJsonAsync<McpServerResponse>();
        Assert.Equal(created.Id, mcpServer!.Id);
    }

    [Fact]
    public async Task GetMcpServerById_Missing_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/mcp-servers/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMcpServer_WithValidData_ReturnsUpdatedServer()
    {
        var created = await CreateMcpServerAsync("Servidor Original", McpServerAuthType.None, null);

        var request = new UpdateMcpServerRequest("Servidor Atualizado", "Nova descrição", "https://mcp.updated.example.com", "None", null);
        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mcpServer = await response.Content.ReadFromJsonAsync<McpServerResponse>();
        Assert.Equal(request.Name, mcpServer!.Name);
        Assert.Equal(request.Description, mcpServer.Description);
        Assert.Equal(request.Url, mcpServer.Url);
    }

    [Fact]
    public async Task UpdateMcpServer_WithoutNewCredential_KeepsExistingCredential()
    {
        var created = await CreateMcpServerAsync("Servidor Com Token", McpServerAuthType.BearerToken, "token-original");

        var request = new UpdateMcpServerRequest("Servidor Com Token", "Descrição", "https://mcp.example.com", "BearerToken", null);
        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var cipher = scope.ServiceProvider.GetRequiredService<IMcpCredentialCipher>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await dbContext.McpServers.SingleAsync(m => m.Id == created.Id);
        Assert.NotNull(persisted.EncryptedCredential);
        Assert.Equal("token-original", cipher.Decrypt(persisted.EncryptedCredential!));
    }

    [Fact]
    public async Task UpdateMcpServer_TransitioningToNone_ClearsPersistedCredential()
    {
        var created = await CreateMcpServerAsync("Servidor A Ser Aberto", McpServerAuthType.BearerToken, "token-a-remover");

        var request = new UpdateMcpServerRequest("Servidor Sem Autenticação", "Descrição", "https://mcp.example.com", "None", null);
        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await dbContext.McpServers.SingleAsync(m => m.Id == created.Id);
        Assert.Null(persisted.EncryptedCredential);
    }

    [Fact]
    public async Task UpdateMcpServer_WithoutNameOrUrl_ReturnsValidationProblem()
    {
        var created = await CreateMcpServerAsync("Servidor", McpServerAuthType.None, null);

        var request = new UpdateMcpServerRequest(null, "Descrição", null, "None", null);
        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMcpServer_Missing_ReturnsNotFound()
    {
        var request = new UpdateMcpServerRequest("Nome", "Descrição", "https://mcp.example.com", "None", null);

        var response = await _client.PutAsJsonAsync($"/mcp-servers/{Guid.NewGuid()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMcpServerById_NeverIncludesCredentialField()
    {
        var created = await CreateMcpServerAsync("Servidor Sigiloso", McpServerAuthType.BearerToken, "outro-segredo");

        var response = await _client.GetAsync($"/mcp-servers/{created.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("outro-segredo", body);
        Assert.DoesNotContain("redential", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListMcpServers_NeverIncludesCredentialField()
    {
        await CreateMcpServerAsync("Servidor Sigiloso 2", McpServerAuthType.BearerToken, "mais-um-segredo");

        var response = await _client.GetAsync("/mcp-servers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("mais-um-segredo", body);
        Assert.DoesNotContain("redential", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeactivateThenActivate_ReflectsIsActive()
    {
        var created = await CreateMcpServerAsync("Servidor Ativável", McpServerAuthType.None, null);
        Assert.True(created.IsActive);

        var deactivateResponse = await _client.PostAsync($"/mcp-servers/{created.Id}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<McpServerResponse>();
        Assert.False(deactivated!.IsActive);

        var activateResponse = await _client.PostAsync($"/mcp-servers/{created.Id}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<McpServerResponse>();
        Assert.True(activated!.IsActive);
    }

    [Fact]
    public async Task DeactivateTwice_IsIdempotent()
    {
        var created = await CreateMcpServerAsync("Servidor Idempotente", McpServerAuthType.None, null);

        var first = await _client.PostAsync($"/mcp-servers/{created.Id}/deactivate", content: null);
        var second = await _client.PostAsync($"/mcp-servers/{created.Id}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False((await second.Content.ReadFromJsonAsync<McpServerResponse>())!.IsActive);
    }

    [Fact]
    public async Task ActivateTwice_IsIdempotent()
    {
        var created = await CreateMcpServerAsync("Servidor Idempotente 2", McpServerAuthType.None, null);

        var first = await _client.PostAsync($"/mcp-servers/{created.Id}/activate", content: null);
        var second = await _client.PostAsync($"/mcp-servers/{created.Id}/activate", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True((await second.Content.ReadFromJsonAsync<McpServerResponse>())!.IsActive);
    }

    [Fact]
    public async Task ActivateAndDeactivate_Missing_ReturnsNotFound()
    {
        var activateResponse = await _client.PostAsync($"/mcp-servers/{Guid.NewGuid()}/activate", content: null);
        var deactivateResponse = await _client.PostAsync($"/mcp-servers/{Guid.NewGuid()}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, activateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deactivateResponse.StatusCode);
    }

    [Fact]
    public async Task InactiveMcpServer_StillAppearsInListAndGetById_AndCanBeUpdated()
    {
        var created = await CreateMcpServerAsync("Servidor Desativado", McpServerAuthType.None, null);
        await _client.PostAsync($"/mcp-servers/{created.Id}/deactivate", content: null);

        var listResponse = await _client.GetAsync("/mcp-servers");
        var mcpServers = await listResponse.Content.ReadFromJsonAsync<List<McpServerResponse>>();
        Assert.False(mcpServers!.Single(m => m.Id == created.Id).IsActive);

        var getResponse = await _client.GetAsync($"/mcp-servers/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.False((await getResponse.Content.ReadFromJsonAsync<McpServerResponse>())!.IsActive);

        var updateRequest = new UpdateMcpServerRequest("Servidor Desativado Editado", "Descrição", "https://mcp.example.com", "None", null);
        var updateResponse = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.False((await updateResponse.Content.ReadFromJsonAsync<McpServerResponse>())!.IsActive);
    }

    private async Task<McpServerResponse> CreateMcpServerAsync(string name, McpServerAuthType authType, string? credential)
    {
        var request = new CreateMcpServerRequest(name, "Descrição", "https://mcp.example.com", authType.ToString(), credential);
        var response = await _client.PostAsJsonAsync("/mcp-servers", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<McpServerResponse>())!;
    }
}
