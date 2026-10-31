using System.Net.Http.Json;
using A2A;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

// Os endereços A2A do agente na resposta da API. O que estes testes protegem é
// menos o formato e mais duas invariantes: que a resposta e o card de
// descoberta nunca anunciem endereços diferentes, e que o endereço saia da url
// pública e nunca do host da requisição (design.md da change agente-enderecos-a2a,
// D1). A url pública é obrigatória desde a change usuarios-locais-login-emissor
// (D3.4), e o caso "sem url, endereço ausente" saiu com ela.
public class AgentA2AAddressesTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetAgentById_ReturnsBothA2AAddresses()
    {
        var created = await CreateAgentAsync("Atendente");

        var agent = await _client.GetFromJsonAsync<AgentResponse>($"/agents/{created.Id}");

        Assert.NotNull(agent!.A2A);
        Assert.Equal($"{ApiFactoryFixture.PublicBaseUrl}/agents/{created.Id}/a2a", agent.A2A!.Url);
        Assert.Equal(
            $"{ApiFactoryFixture.PublicBaseUrl}/agents/{created.Id}/.well-known/agent-card.json",
            agent.A2A.AgentCardUrl);
    }

    [Fact]
    public async Task ListAgents_CarriesTheSameAddressesAsGetById()
    {
        var created = await CreateAgentAsync("Atendente");

        var fetched = await _client.GetFromJsonAsync<AgentResponse>($"/agents/{created.Id}");
        var agents = await _client.GetFromJsonAsync<List<AgentResponse>>("/agents");
        var listed = agents!.Single(agent => agent.Id == created.Id);

        Assert.Equal(fetched!.A2A!.Url, listed.A2A!.Url);
        Assert.Equal(fetched.A2A.AgentCardUrl, listed.A2A.AgentCardUrl);
    }

    [Fact]
    public async Task ResponseAddress_MatchesTheOneAnnouncedByTheDiscoveryCard()
    {
        var created = await CreateAgentAsync("Atendente");

        var agent = await _client.GetFromJsonAsync<AgentResponse>($"/agents/{created.Id}");
        var card = await _client.GetFromJsonAsync<AgentCard>(agent!.A2A!.AgentCardUrl);

        // Dois lugares montando a mesma url divergirem seria a falha mais
        // provável desta mudança, e a mais difícil de perceber.
        Assert.Equal(agent.A2A.Url, card!.SupportedInterfaces.Single().Url);
    }

    [Fact]
    public async Task InactiveAgent_StillCarriesTheAddresses()
    {
        var created = await CreateAgentAsync("Atendente");
        await _client.PostAsync($"/agents/{created.Id}/deactivate", content: null);

        var agent = await _client.GetFromJsonAsync<AgentResponse>($"/agents/{created.Id}");

        // O agente inativo continua descobrível: os endereços descrevem onde
        // ele é alcançável, não se ele aceitará o que receber.
        Assert.False(agent!.IsActive);
        Assert.NotNull(agent.A2A);
    }

    [Fact]
    public async Task Addresses_DoNotComeFromTheRequestHost()
    {
        var created = await CreateAgentAsync("Atendente");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/agents/{created.Id}");
        request.Headers.Host = "outro-host.exemplo";

        var response = await _client.SendAsync(request);
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();

        Assert.StartsWith($"{ApiFactoryFixture.PublicBaseUrl}/", agent!.A2A!.Url);
        Assert.StartsWith($"{ApiFactoryFixture.PublicBaseUrl}/", agent.A2A.AgentCardUrl);
    }

    private async Task<AgentResponse> CreateAgentAsync(string name)
    {
        var response = await _client.PostAsJsonAsync(
            "/agents",
            new CreateAgentRequest(name, "Instruções.", Provider, Model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }
}
