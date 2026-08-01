using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AgentCreateProviderValidationTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateAgent_WithoutProviderOrModel_ReturnsValidationProblem()
    {
        var request = new CreateAgentRequest("Atendente", "Instruções.", null, null);

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAgent_WithProviderNotConfigured_ReturnsValidationProblem()
    {
        // Nem Anthropic nem Gemini têm variável de ambiente configurada em
        // appsettings.Development.json — rejeitado independentemente do model.
        var request = new CreateAgentRequest("Atendente", "Instruções.", "anthropic", "claude-opus-5");

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAgent_WithModelNotInProviderCatalog_ReturnsValidationProblem()
    {
        // openai está configurado, mas "modelo-inexistente" não consta no catálogo curado.
        var request = new CreateAgentRequest("Atendente", "Instruções.", "openai", "modelo-inexistente");

        var response = await _client.PostAsJsonAsync("/agents", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
