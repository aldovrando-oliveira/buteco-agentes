using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Tests.Knowledge;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

/// <summary>
/// "Rotas só de serviço recusam o token do painel" (spec <c>route-roles</c>; design.md da change
/// papeis-no-token, Q8f): <c>POST /agents/{id}/a2a</c> e <c>/sync/knowledge-bases/*</c> não estão
/// na tabela de papéis. O painel, que não as chama, recebe <c>403</c> nelas com qualquer papel, e os
/// subjects de serviço continuam passando. Até a #184, o <c>operator</c> passava em tudo.
/// </summary>
public class RoleRoutesServiceOnlyTests(AgentDeactivationFixture fixture) : IClassFixture<AgentDeactivationFixture>
{
    private readonly HttpClient _panelAdmin = fixture.CreateClient();

    [Fact]
    public async Task PanelAdmin_IsForbiddenOnTheSyncRoutes_AndWritesNothing()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(fixture.Services);

        var list = await _panelAdmin.GetAsync("/sync/knowledge-bases/");
        var upsert = await _panelAdmin.UpsertAsync(knowledgeBase.Id, "doc-do-painel");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, upsert.StatusCode);
        var documents = await _panelAdmin.GetFromJsonAsync<JsonElement>($"/knowledge-bases/{knowledgeBase.Id}/documents");
        Assert.Equal(0, documents.GetArrayLength());
    }

    [Fact]
    public async Task PanelAdmin_IsForbiddenOnTheA2ARoute_AndNoJobIsPublished()
    {
        var agentId = await CreateAgentAsync();

        var response = await _panelAdmin.PostAsJsonAsync($"/agents/{agentId}/a2a", SendMessage());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(fixture.TaskJobPublisher.PublishedMessages, message => message.AgentId == agentId);
    }

    [Fact]
    public async Task TheServices_KeepTheirRoutes()
    {
        var agentId = await CreateAgentAsync();
        var inbox = TestAuthentication.InboxServiceClient(fixture);
        var connectors = KnowledgeSyncTestSeed.CreateConnectorsClient(fixture);

        var a2a = await inbox.PostAsJsonAsync($"/agents/{agentId}/a2a", SendMessage());
        var sync = await connectors.GetAsync("/sync/knowledge-bases/");

        Assert.Equal(HttpStatusCode.OK, a2a.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
    }

    private static object SendMessage() => new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "SendMessage",
        @params = new
        {
            message = new { role = "ROLE_USER", parts = new[] { new { text = "Olá" } }, messageId = Guid.NewGuid().ToString("N") },
        },
    };

    private async Task<Guid> CreateAgentAsync()
    {
        var response = await _panelAdmin.PostAsJsonAsync(
            "/agents",
            new { name = "Agente das rotas de serviço", instructions = "Responda.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
