using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.AgentMcpBindings.Requests;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.McpServers.Security;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

/// <summary>
/// Conexão e transições do tipo CustomHeader com o servidor MCP simulado
/// (change mcp-autenticacao-header-customizado, D3 e D4). Toda asserção sobre
/// segredo lê o TEXTO BRUTO da resposta, e toda asserção sobre o que o servidor
/// recebeu lê os headers registrados pelo fake, não o resultado desserializado.
/// </summary>
public class McpServerCustomHeaderConnectionTests(McpConnectionTestFixture factory) : IClassFixture<McpConnectionTestFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private readonly HttpClient _client = factory.CreateClient();

    // --- Envio do header (6.4, 6.5) --------------------------------------

    // Guarda da tarefa 6.5: a afirmação que protege o tipo é a NEGATIVA —
    // nenhuma requisição levou Authorization. Montar os dois headers no switch
    // de BuildTransport continuaria passando no "sucesso", e só esta asserção pega.
    [Fact]
    public async Task TestUnsaved_CustomHeader_SendsTheTokenInTheConfiguredHeader_AndNeverAuthorization()
    {
        ResetHandler(required: ("X-API-TOKEN", "SEGREDO-abc"));
        var requestsBefore = factory.McpServerHandler.ReceivedHeaders.Count;

        var response = await _client.PostAsJsonAsync(
            "/mcp-servers/test",
            new TestMcpServerConfigRequest("https://mcp.example.com", "CustomHeader", "SEGREDO-abc", "X-API-TOKEN"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.True(result!.Success);

        var received = factory.McpServerHandler.ReceivedHeaders.Skip(requestsBefore).ToList();
        Assert.NotEmpty(received);
        Assert.All(received, headers => Assert.Equal("SEGREDO-abc", headers["X-API-TOKEN"]));
        Assert.All(received, headers => Assert.False(headers.ContainsKey("Authorization")));
    }

    [Fact]
    public async Task TestUnsaved_CustomHeaderRejectedByServer_ReturnsCredentialRejected()
    {
        ResetHandler(required: ("X-API-TOKEN", "SEGREDO-certo"));

        var response = await _client.PostAsJsonAsync(
            "/mcp-servers/test",
            new TestMcpServerConfigRequest("https://mcp.example.com", "CustomHeader", "SEGREDO-errado", "X-API-TOKEN"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.False(result!.Success);
        Assert.Equal(McpConnectionTestFailureReason.CredentialRejected, result.FailureReason);
    }

    [Fact]
    public async Task TestSaved_CustomHeader_SendsThePersistedTokenInThePersistedHeader()
    {
        ResetHandler(required: ("X-Salvo", "SEGREDO-salvo"));
        var created = await CreateCustomHeaderServerAsync("Header Salvo", "X-Salvo", "SEGREDO-salvo");

        var response = await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null);

        var result = await response.Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.True(result!.Success);
    }

    [Fact]
    public async Task ListTools_CustomHeader_ReturnsTheServerTools()
    {
        ResetHandler(required: ("X-Tools", "SEGREDO-tools"));
        factory.McpServerHandler.AvailableTools = [new FakeMcpTool("read_file")];
        var created = await CreateCustomHeaderServerAsync("Header Tools", "X-Tools", "SEGREDO-tools");

        var response = await _client.GetAsync($"/mcp-servers/{created.Id}/tools");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<McpServerToolsResponse>();
        Assert.True(result!.Success);
        Assert.Contains(result.Tools!, tool => tool.Name == "read_file");
    }

    // --- Linha salva com nome recusado (6.5a, guarda) ---------------------

    // Sem a checagem de McpConnectionTester (6.2a), a linha chega ao SDK, que
    // lança com o VALOR do header na mensagem (V2), e os três caminhos devolvem
    // essa mensagem na resposta HTTP (V9).
    [Fact]
    public async Task SavedRowWithHeaderNameRejectedByDotNet_NeverLeaksTheSecret_OnAnyOfTheThreePaths()
    {
        ResetHandler();
        factory.McpServerHandler.AvailableTools = [new FakeMcpTool("read_file")];
        var mcpServerId = await InsertCustomHeaderRowAsync("Linha Content-Type", "Content-Type", "SEGREDO-linha-salva");
        var requestsBefore = factory.McpServerHandler.RequestCount;

        var toolsResponse = await _client.GetAsync($"/mcp-servers/{mcpServerId}/tools");
        var toolsBody = await toolsResponse.Content.ReadAsStringAsync();
        var testResponse = await _client.PostAsync($"/mcp-servers/{mcpServerId}/test", content: null);
        var testBody = await testResponse.Content.ReadAsStringAsync();

        var agent = await CreateAgentAsync("Agente Linha Content-Type");
        var bindingResponse = await _client.PutAsJsonAsync(
            $"/agents/{agent.Id}/mcp-servers",
            new ReplaceAgentMcpServersRequest([new AgentMcpServerBindingRequest(mcpServerId, ["read_file"])]));
        var bindingBody = await bindingResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("SEGREDO-linha-salva", toolsBody);
        Assert.DoesNotContain("SEGREDO-linha-salva", testBody);
        Assert.DoesNotContain("SEGREDO-linha-salva", bindingBody);

        Assert.Equal("Unknown", FailureReasonOf(toolsBody));
        Assert.Equal("Unknown", FailureReasonOf(testBody));
        Assert.Equal(HttpStatusCode.BadGateway, bindingResponse.StatusCode);
        Assert.Equal(requestsBefore, factory.McpServerHandler.RequestCount);
    }

    // --- Transições do update (5.3, 5.4) ----------------------------------

    [Fact]
    public async Task Update_CustomHeaderToCustomHeader_WithNewHeaderNameAndBlankCredential_KeepsTheSecretInTheNewHeader()
    {
        ResetHandler();
        var created = await CreateCustomHeaderServerAsync("Header Renomeado", "X-Antigo", "SEGREDO-mantido");

        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", UpdateRequest(created, "CustomHeader", null, "X-Novo"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("X-Novo", (await response.Content.ReadFromJsonAsync<McpServerResponse>())!.AuthHeaderName);

        factory.McpServerHandler.RequiredHeader = ("X-Novo", "SEGREDO-mantido");
        var test = await (await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null)).Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.True(test!.Success);
    }

    [Fact]
    public async Task Update_CustomHeaderToNone_ClearsCredentialAndHeaderName()
    {
        ResetHandler();
        var created = await CreateCustomHeaderServerAsync("Header Para Nenhuma", "X-API-TOKEN", "SEGREDO-apagado");

        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", UpdateRequest(created, "None", null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var persisted = await ReadPersistedAsync(created.Id);
        Assert.Equal(McpServerAuthType.None, persisted.AuthType);
        Assert.Null(persisted.EncryptedCredential);
        Assert.Null(persisted.AuthHeaderName);
    }

    [Fact]
    public async Task Update_BearerToCustomHeader_WithNewCredential_PersistsBoth()
    {
        ResetHandler();
        var created = await CreateServerAsync("Bearer Para Header", "BearerToken", "SEGREDO-bearer-antigo", null);

        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", UpdateRequest(created, "CustomHeader", "SEGREDO-header-novo", "X-API-TOKEN"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var persisted = await ReadPersistedAsync(created.Id);
        Assert.Equal(McpServerAuthType.CustomHeader, persisted.AuthType);
        Assert.Equal("X-API-TOKEN", persisted.AuthHeaderName);
        Assert.Equal("SEGREDO-header-novo", Decrypt(persisted.EncryptedCredential!));
    }

    [Fact]
    public async Task Update_CustomHeaderToBearer_WithNewCredential_ClearsTheHeaderName()
    {
        ResetHandler();
        var created = await CreateCustomHeaderServerAsync("Header Para Bearer", "X-API-TOKEN", "SEGREDO-header-antigo");

        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", UpdateRequest(created, "BearerToken", "SEGREDO-bearer-novo", "X-API-TOKEN"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<McpServerResponse>())!.AuthHeaderName);
        var persisted = await ReadPersistedAsync(created.Id);
        Assert.Equal(McpServerAuthType.BearerToken, persisted.AuthType);
        Assert.Null(persisted.AuthHeaderName);
        Assert.Equal("SEGREDO-bearer-novo", Decrypt(persisted.EncryptedCredential!));
    }

    // Guarda da tarefa 5.4 (D4): trocar entre dois tipos com credencial sem
    // credencial nova é 400, e o registro persistido NÃO muda — afirmado pelo
    // banco e por um re-teste salvo que ainda envia o segredo anterior.
    [Fact]
    public async Task Update_BearerToCustomHeader_WithoutCredential_Returns400AndKeepsTheRecordUntouched()
    {
        ResetHandler();
        var created = await CreateServerAsync("Bearer Sem Credencial Nova", "BearerToken", "SEGREDO-bearer-intacto", null);

        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", UpdateRequest(created, "CustomHeader", null, "X-API-TOKEN"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"credential\"", await response.Content.ReadAsStringAsync());

        var persisted = await ReadPersistedAsync(created.Id);
        Assert.Equal(McpServerAuthType.BearerToken, persisted.AuthType);
        Assert.Null(persisted.AuthHeaderName);

        factory.McpServerHandler.RequiredBearerToken = "SEGREDO-bearer-intacto";
        var test = await (await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null)).Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.True(test!.Success);
    }

    [Fact]
    public async Task Update_CustomHeaderToBearer_WithoutCredential_Returns400AndKeepsTheRecordUntouched()
    {
        ResetHandler();
        var created = await CreateCustomHeaderServerAsync("Header Sem Credencial Nova", "X-Intacto", "SEGREDO-header-intacto");

        var response = await _client.PutAsJsonAsync($"/mcp-servers/{created.Id}", UpdateRequest(created, "BearerToken", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"credential\"", await response.Content.ReadAsStringAsync());

        var persisted = await ReadPersistedAsync(created.Id);
        Assert.Equal(McpServerAuthType.CustomHeader, persisted.AuthType);
        Assert.Equal("X-Intacto", persisted.AuthHeaderName);

        factory.McpServerHandler.RequiredHeader = ("X-Intacto", "SEGREDO-header-intacto");
        var test = await (await _client.PostAsync($"/mcp-servers/{created.Id}/test", content: null)).Content.ReadFromJsonAsync<McpConnectionTestResult>();
        Assert.True(test!.Success);
    }

    private static UpdateMcpServerRequest UpdateRequest(McpServerResponse current, string authType, string? credential, string? authHeaderName) =>
        new(current.Name, current.Description, current.Url, authType, credential, authHeaderName);

    private Task<McpServerResponse> CreateCustomHeaderServerAsync(string name, string headerName, string credential) =>
        CreateServerAsync(name, "CustomHeader", credential, headerName);

    private async Task<McpServerResponse> CreateServerAsync(string name, string authType, string? credential, string? authHeaderName)
    {
        var response = await _client.PostAsJsonAsync(
            "/mcp-servers",
            new CreateMcpServerRequest(name, "Descrição", "https://mcp.example.com", authType, credential, authHeaderName));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<McpServerResponse>())!;
    }

    // Linha gravada por SQL direto, sem passar pela validação de entrada — o
    // caso que a checagem do McpConnectionTester existe para cobrir (V9).
    private async Task<Guid> InsertCustomHeaderRowAsync(string name, string headerName, string credential)
    {
        var id = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var encrypted = scope.ServiceProvider.GetRequiredService<IMcpCredentialCipher>().Encrypt(credential);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO mcp_servers ("Id", "Name", "Description", "Url", "AuthType", "EncryptedCredential", "AuthHeaderName", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {name}, 'Descrição', 'https://mcp.example.com', 'CustomHeader', {encrypted}, {headerName}, true, now(), now())
            """);
        return id;
    }

    private async Task<McpServer> ReadPersistedAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().McpServers.AsNoTracking().SingleAsync(server => server.Id == id);
    }

    private string Decrypt(string encrypted)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMcpCredentialCipher>().Decrypt(encrypted);
    }

    private async Task<AgentResponse> CreateAgentAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest(name, "Instruções.", Provider, Model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }

    private static string? FailureReasonOf(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("failureReason").GetString();
    }

    private void ResetHandler((string Name, string Value)? required = null)
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;
        factory.McpServerHandler.UnreachableUrls.Clear();
        factory.McpServerHandler.AvailableTools = [];
        factory.McpServerHandler.RequiredHeader = required;
    }
}
