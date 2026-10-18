using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

/// <summary>
/// Validação do tipo CustomHeader pelos endpoints (change
/// mcp-autenticacao-header-customizado, D3 e D9). Usa
/// <see cref="McpConnectionTestFixture"/> porque o teste não salvo precisa
/// afirmar que NENHUMA requisição chegou ao servidor MCP: um nome que o .NET
/// recusa, se chegasse ao SDK, viraria exceção com o segredo na mensagem
/// (design.md, V2 e V3).
/// </summary>
public class McpAuthHeaderValidationTests(McpConnectionTestFixture factory) : IClassFixture<McpConnectionTestFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    // --- Nome do header (D3, regras 1 a 3) --------------------------------

    [Theory]
    [InlineData("X-API-TOKEN")]
    [InlineData("Authorization")]
    public async Task CreateCustomHeader_AcceptedHeaderName_IsCreated(string headerName)
    {
        var request = CustomHeaderRequest($"Aceito {headerName}", headerName, "SEGREDO-aceito");

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<McpServerResponse>();
        Assert.Equal(headerName, created!.AuthHeaderName);
    }

    [Theory]
    [InlineData("X API")]
    [InlineData("Content-Type")]
    [InlineData("mcp-session-id")]
    [InlineData("MCP-Protocol-Version")]
    [InlineData("Host")]
    [InlineData("Accept")]
    [InlineData("Transfer-Encoding")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CreateCustomHeader_RejectedHeaderName_ReturnsValidationProblemWithoutCreating(string? headerName)
    {
        var serverName = $"Recusado {Guid.NewGuid():N}";
        var request = CustomHeaderRequest(serverName, headerName, "SEGREDO-recusado");

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("authHeaderName", ErrorKeys(body));
        Assert.DoesNotContain("SEGREDO-recusado", body);
        Assert.False(await ServerExistsAsync(serverName));
    }

    [Fact]
    public async Task CreateBearerToken_WithAuthHeaderName_IgnoresTheHeaderName()
    {
        var request = new CreateMcpServerRequest("Bearer Com Nome", "Descrição", "https://mcp.example.com", "BearerToken", "SEGREDO-bearer", "X-API-TOKEN");

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<McpServerResponse>();
        Assert.Null(created!.AuthHeaderName);
    }

    // --- Valor do token (D3) ----------------------------------------------

    [Theory]
    [InlineData("SEGREDO-crlf\r\nX-Injetado: 1")]
    [InlineData("SEGREDO-çã")]
    [InlineData(" SEGREDO-ponta")]
    [InlineData("SEGREDO-ponta ")]
    public async Task CreateCustomHeader_InvalidCredentialValue_ReturnsValidationProblemWithoutTheValue(string credential)
    {
        var serverName = $"Valor Ruim {Guid.NewGuid():N}";
        var request = CustomHeaderRequest(serverName, "X-API-TOKEN", credential);

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("credential", ErrorKeys(body));
        Assert.DoesNotContain("SEGREDO-", body);
        Assert.False(await ServerExistsAsync(serverName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task TestUnsavedCustomHeader_WithoutCredential_ReturnsValidationProblemWithoutConnecting(string? credential)
    {
        ResetHandler();
        var requestCountBefore = factory.McpServerHandler.RequestCount;

        var request = new TestMcpServerConfigRequest("https://mcp.example.com", "CustomHeader", credential, "X-API-TOKEN");
        var response = await _client.PostAsJsonAsync("/mcp-servers/test", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("credential", ErrorKeys(await response.Content.ReadAsStringAsync()));
        Assert.Equal(requestCountBefore, factory.McpServerHandler.RequestCount);
    }

    [Fact]
    public async Task TestUnsavedCustomHeader_NonAsciiCredential_ReturnsValidationProblemWithoutConnecting()
    {
        ResetHandler();
        var requestCountBefore = factory.McpServerHandler.RequestCount;

        var request = new TestMcpServerConfigRequest("https://mcp.example.com", "CustomHeader", "SEGREDO-çã", "X-API-TOKEN");
        var response = await _client.PostAsJsonAsync("/mcp-servers/test", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("credential", ErrorKeys(body));
        Assert.DoesNotContain("SEGREDO-", body);
        Assert.Equal(requestCountBefore, factory.McpServerHandler.RequestCount);
    }

    // Guarda da tarefa 4.6 (convenção 15): sem a validação de entrada, este
    // pedido chega ao SDK, que lança InvalidOperationException com o VALOR do
    // header na mensagem, e o McpConnectionTester devolve essa mensagem num 200
    // (V2, V3). A asserção lê o texto bruto, não um objeto desserializado.
    [Fact]
    public async Task TestUnsavedCustomHeader_HeaderNameRejectedByDotNet_Returns400WithoutTheSecretAndWithoutConnecting()
    {
        ResetHandler();
        var requestCountBefore = factory.McpServerHandler.RequestCount;

        var request = new TestMcpServerConfigRequest("https://mcp.example.com", "CustomHeader", "SEGREDO-teste-nao-salvo", "Content-Type");
        var response = await _client.PostAsJsonAsync("/mcp-servers/test", request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("SEGREDO-teste-nao-salvo", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("authHeaderName", ErrorKeys(body));
        Assert.Equal(requestCountBefore, factory.McpServerHandler.RequestCount);
    }

    // --- authType por nome de membro (D9) -------------------------------

    // Guarda da tarefa 4.7: Enum.TryParse aceitava os três (V7).
    [Theory]
    [InlineData("2")]
    [InlineData("99")]
    [InlineData("None,BearerToken")]
    public async Task Create_AuthTypeThatIsNotAMemberName_ReturnsValidationProblemWithoutCreating(string authType)
    {
        var serverName = $"Tipo Frouxo {Guid.NewGuid():N}";
        var request = new CreateMcpServerRequest(serverName, "Descrição", "https://mcp.example.com", authType, "SEGREDO-tipo", "X-API-TOKEN");

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("authType", ErrorKeys(await response.Content.ReadAsStringAsync()));
        Assert.False(await ServerExistsAsync(serverName));
    }

    [Fact]
    public async Task Create_UnknownAuthType_MessageListsTheThreeTypes()
    {
        var request = new CreateMcpServerRequest("Tipo Basic", "Descrição", "https://mcp.example.com", "Basic", "SEGREDO-basic");

        var response = await _client.PostAsJsonAsync("/mcp-servers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = document.RootElement.GetProperty("errors").GetProperty("authType")[0].GetString();
        Assert.Contains("None", message);
        Assert.Contains("BearerToken", message);
        Assert.Contains("CustomHeader", message);
    }

    private static CreateMcpServerRequest CustomHeaderRequest(string name, string? headerName, string credential) =>
        new(name, "Descrição", "https://mcp.example.com", "CustomHeader", credential, headerName);

    private static IReadOnlyList<string> ErrorKeys(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToList();
    }

    private async Task<bool> ServerExistsAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.McpServers.AnyAsync(server => server.Name == name);
    }

    private void ResetHandler()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;
        factory.McpServerHandler.UnreachableUrls.Clear();
    }
}
