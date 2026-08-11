using System.Net;
using System.Net.Http.Json;
using Buteco.Inbox.Channels.Adapters.Testing;
using Buteco.Inbox.Channels.Requests;
using Buteco.Inbox.Channels.Responses;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Tests;

public class ChannelEndpointsTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateChannel_WithValidAgent_ReturnsCreatedChannel()
    {
        ResetAgentApiHandler();
        var agentId = RegisterExistingAgent();

        var request = new CreateChannelRequest("test-channel", "Canal Principal", "s3cr3t-token", agentId);
        var response = await _client.PostAsJsonAsync("/channels", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var channel = await response.Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.NotNull(channel);
        Assert.NotEqual(Guid.Empty, channel.Id);
        Assert.Equal("test-channel", channel.ChannelType);
        Assert.Equal(request.Name, channel.Name);
        Assert.Equal(agentId, channel.AgentId);
        Assert.True(channel.IsActive);

        var publicUrlBaseUrl = factory.Services.GetRequiredService<IOptions<PublicUrlOptions>>().Value.BaseUrl;
        Assert.Equal($"{publicUrlBaseUrl.TrimEnd('/')}/webhooks/test-channel/{channel.Id}", channel.WebhookUrl);
    }

    [Fact]
    public async Task CreateChannel_WithCredentialRejectedByAdapterValidator_ReturnsValidationProblem()
    {
        // Prova que o registro DI keyed resolve o validador certo para o
        // ChannelType certo, chamado pelo Command antes de criptografar
        // (design.md, Decision 2) — não uma checagem hardcoded no endpoint.
        ResetAgentApiHandler();
        var agentId = RegisterExistingAgent();

        var request = new CreateChannelRequest("test-channel", "Canal Com Credencial Inválida", TestChannelConfigValidator.RejectedCredential, agentId);
        var response = await _client.PostAsJsonAsync("/channels", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateChannel_NeverIncludesCredentialField()
    {
        ResetAgentApiHandler();
        var agentId = RegisterExistingAgent();

        var request = new CreateChannelRequest("test-channel", "Canal Sigiloso", "s3gr3d0-secreto", agentId);
        var response = await _client.PostAsJsonAsync("/channels", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("s3gr3d0-secreto", body);
        Assert.DoesNotContain("redential", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateChannel_WithoutNameChannelTypeOrCredential_ReturnsValidationProblem()
    {
        ResetAgentApiHandler();

        var request = new CreateChannelRequest(null, null, null, Guid.NewGuid());
        var response = await _client.PostAsJsonAsync("/channels", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateChannel_WithInvalidChannelType_ReturnsValidationProblem()
    {
        ResetAgentApiHandler();
        var agentId = RegisterExistingAgent();

        var request = new CreateChannelRequest("SMS", "Canal Inválido", "token", agentId);
        var response = await _client.PostAsJsonAsync("/channels", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateChannel_WithAgentNotFound_ReturnsValidationProblem()
    {
        ResetAgentApiHandler();

        var request = new CreateChannelRequest("test-channel", "Canal Sem Agente", "token", Guid.NewGuid());
        var response = await _client.PostAsJsonAsync("/channels", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateChannel_WithApiRespondingServerError_ReturnsError()
    {
        // apps/api respondendo HTTP 500 — resposta HTTP completa, distinta
        // de host inalcançável (design.md, Decision 4).
        ResetAgentApiHandler();
        factory.AgentApiHandler.SimulateServerError = true;

        var request = new CreateChannelRequest("test-channel", "Canal Com API Instável", "token", Guid.NewGuid());
        var response = await _client.PostAsJsonAsync("/channels", request);

        Assert.False(response.IsSuccessStatusCode);
        Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateChannel_WithApiUnreachable_ReturnsError()
    {
        ResetAgentApiHandler();
        factory.AgentApiHandler.SimulateUnreachable = true;

        var request = new CreateChannelRequest("test-channel", "Canal Com API Fora Do Ar", "token", Guid.NewGuid());
        var response = await _client.PostAsJsonAsync("/channels", request);

        Assert.False(response.IsSuccessStatusCode);
        Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateChannel_WithInactiveAgent_IsPermitted()
    {
        ResetAgentApiHandler();
        var agentId = RegisterExistingAgent(isActive: false);

        var request = new CreateChannelRequest("test-channel", "Canal Com Agente Inativo", "token", agentId);
        var response = await _client.PostAsJsonAsync("/channels", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var channel = await response.Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.Equal(agentId, channel!.AgentId);
    }

    [Fact]
    public async Task ListChannels_IncludesPreviouslyCreatedChannel()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Listado");

        var response = await _client.GetAsync("/channels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var channels = await response.Content.ReadFromJsonAsync<List<ChannelResponse>>();
        Assert.NotNull(channels);
        Assert.Contains(channels, c => c.Id == created.Id);
    }

    [Fact]
    public async Task ListChannels_NeverIncludesCredentialField()
    {
        ResetAgentApiHandler();
        await CreateChannelAsync("Canal Sigiloso Na Lista", credential: "outro-segredo-da-lista");

        var response = await _client.GetAsync("/channels");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("outro-segredo-da-lista", body);
        Assert.DoesNotContain("redential", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetChannelById_Existing_ReturnsChannel()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Consultado");

        var response = await _client.GetAsync($"/channels/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var channel = await response.Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.Equal(created.Id, channel!.Id);
    }

    [Fact]
    public async Task GetChannelById_Missing_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/channels/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetChannelById_NeverIncludesCredentialField()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Sigiloso Individual", credential: "segredo-individual");

        var response = await _client.GetAsync($"/channels/{created.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("segredo-individual", body);
        Assert.DoesNotContain("redential", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateChannel_WithValidData_ReturnsUpdatedChannel()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Original");

        var request = new UpdateChannelRequest("Canal Atualizado", null, created.AgentId);
        var response = await _client.PutAsJsonAsync($"/channels/{created.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var channel = await response.Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.Equal("Canal Atualizado", channel!.Name);
    }

    [Fact]
    public async Task UpdateChannel_WithoutNewCredential_KeepsExistingCredential()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Com Credencial Original", credential: "credencial-original");

        var request = new UpdateChannelRequest("Canal Com Credencial Original", null, created.AgentId);
        var updateResponse = await _client.PutAsJsonAsync($"/channels/{created.Id}", request);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var cipher = scope.ServiceProvider.GetRequiredService<IChannelCredentialCipher>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await dbContext.Channels.SingleAsync(c => c.Id == created.Id);
        Assert.Equal("credencial-original", cipher.Decrypt(persisted.EncryptedCredentials));
    }

    [Fact]
    public async Task UpdateChannel_WithNewCredential_ReplacesCredential()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal A Trocar Credencial", credential: "credencial-velha");

        var request = new UpdateChannelRequest("Canal A Trocar Credencial", "credencial-nova", created.AgentId);
        var updateResponse = await _client.PutAsJsonAsync($"/channels/{created.Id}", request);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var cipher = scope.ServiceProvider.GetRequiredService<IChannelCredentialCipher>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await dbContext.Channels.SingleAsync(c => c.Id == created.Id);
        Assert.Equal("credencial-nova", cipher.Decrypt(persisted.EncryptedCredentials));
    }

    [Fact]
    public async Task UpdateChannel_ChangingAgentIdToNonexistent_ReturnsValidationProblem()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Trocando De Agente");

        var request = new UpdateChannelRequest("Canal Trocando De Agente", null, Guid.NewGuid());
        var response = await _client.PutAsJsonAsync($"/channels/{created.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateChannel_ChangingToInactiveAgent_IsPermitted()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Trocando Para Agente Inativo");
        var inactiveAgentId = RegisterExistingAgent(isActive: false);

        var request = new UpdateChannelRequest("Canal Trocando Para Agente Inativo", null, inactiveAgentId);
        var response = await _client.PutAsJsonAsync($"/channels/{created.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var channel = await response.Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.Equal(inactiveAgentId, channel!.AgentId);
    }

    [Fact]
    public async Task UpdateChannel_Missing_ReturnsNotFound()
    {
        ResetAgentApiHandler();
        var agentId = RegisterExistingAgent();

        var request = new UpdateChannelRequest("Nome", null, agentId);
        var response = await _client.PutAsJsonAsync($"/channels/{Guid.NewGuid()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateChannel_WithoutNameOrAgentId_ReturnsValidationProblem()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Para Validação");

        var request = new UpdateChannelRequest(null, null, null);
        var response = await _client.PutAsJsonAsync($"/channels/{created.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateThenActivate_ReflectsIsActive()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Ativável");
        Assert.True(created.IsActive);

        var deactivateResponse = await _client.PostAsync($"/channels/{created.Id}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.False(deactivated!.IsActive);

        var activateResponse = await _client.PostAsync($"/channels/{created.Id}/activate", content: null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<ChannelResponse>();
        Assert.True(activated!.IsActive);
    }

    [Fact]
    public async Task DeactivateTwice_IsIdempotent()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Idempotente");

        var first = await _client.PostAsync($"/channels/{created.Id}/deactivate", content: null);
        var second = await _client.PostAsync($"/channels/{created.Id}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False((await second.Content.ReadFromJsonAsync<ChannelResponse>())!.IsActive);
    }

    [Fact]
    public async Task ActivateTwice_IsIdempotent()
    {
        ResetAgentApiHandler();
        var created = await CreateChannelAsync("Canal Idempotente 2");

        var first = await _client.PostAsync($"/channels/{created.Id}/activate", content: null);
        var second = await _client.PostAsync($"/channels/{created.Id}/activate", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True((await second.Content.ReadFromJsonAsync<ChannelResponse>())!.IsActive);
    }

    [Fact]
    public async Task ActivateAndDeactivate_Missing_ReturnsNotFound()
    {
        var activateResponse = await _client.PostAsync($"/channels/{Guid.NewGuid()}/activate", content: null);
        var deactivateResponse = await _client.PostAsync($"/channels/{Guid.NewGuid()}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, activateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deactivateResponse.StatusCode);
    }

    private void ResetAgentApiHandler()
    {
        factory.AgentApiHandler.SimulateServerError = false;
        factory.AgentApiHandler.SimulateUnreachable = false;
        factory.AgentApiHandler.ExistingAgentIsActive = true;
    }

    private Guid RegisterExistingAgent(bool isActive = true)
    {
        var agentId = Guid.NewGuid();
        factory.AgentApiHandler.ExistingAgentIds.Add(agentId);
        factory.AgentApiHandler.ExistingAgentIsActive = isActive;
        return agentId;
    }

    private async Task<ChannelResponse> CreateChannelAsync(string name, string credential = "credencial-padrao")
    {
        var agentId = RegisterExistingAgent();
        var request = new CreateChannelRequest("test-channel", name, credential, agentId);
        var response = await _client.PostAsJsonAsync("/channels", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelResponse>())!;
    }
}
