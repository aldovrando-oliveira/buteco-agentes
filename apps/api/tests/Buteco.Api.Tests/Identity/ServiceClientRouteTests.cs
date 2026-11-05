using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Os clientes de serviço nas rotas do <c>apps/api</c> (spec <c>route-authentication</c>,
/// "apps/api aceita só o access token do emissor"; design.md da change
/// clientes-servico-emissor, D3), com tokens do <c>/connect/token</c> real: cada cliente passa
/// só nas rotas do subject dele, e recebe <c>403</c> nas outras.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com o <c>MapPrincipal</c> tirando o subject do
/// <c>sub</c> em vez do <c>client_id</c>, os casos <c>200</c> reprovam com <c>403</c>, porque
/// no <c>client_credentials</c> o <c>sub</c> é o próprio <c>client_id</c>, que não está na
/// tabela de subjects (registro no tasks.md, 4.3).
/// </para>
/// </summary>
public class ServiceClientRouteTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => ServiceClientTokens.EnsureAsync(factory.Services);

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> ClientOfAsync(string clientId)
    {
        var client = ServiceClientTokens.AnonymousClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await ServiceClientTokens.AccessTokenAsync(factory, clientId));
        return client;
    }

    [Fact]
    public async Task TheInboxClient_ReadsAnAgent_AndIsForbiddenOutsideItsRoutes()
    {
        var agentId = await CreateAgentAsync();
        var inbox = await ClientOfAsync(IdentityClients.InboxServiceClientId);

        Assert.Equal(HttpStatusCode.OK, (await inbox.GetAsync($"/agents/{agentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await inbox.GetAsync("/agents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await inbox.GetAsync("/sync/knowledge-bases/")).StatusCode);
    }

    [Fact]
    public async Task TheConnectorsClient_ListsTheSyncedBases_AndIsForbiddenOutsideItsRoutes()
    {
        var connectors = await ClientOfAsync(IdentityClients.ConnectorsServiceClientId);

        Assert.Equal(HttpStatusCode.OK, (await connectors.GetAsync("/sync/knowledge-bases/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await connectors.GetAsync("/agents")).StatusCode);
    }

    // Pelo cliente padrão da fixture, o do operador.
    private async Task<Guid> CreateAgentAsync()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/agents",
            new { name = "Agente do cliente de serviço", instructions = "Instrução.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
