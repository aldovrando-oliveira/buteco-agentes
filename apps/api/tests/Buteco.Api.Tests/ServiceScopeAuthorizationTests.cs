using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buteco.Api.Auth;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

// Cobertura de ServiceScopeAuthorizationHandler (design.md, Decision 3):
// um token com sub "service:inbox" só é autorizado nas duas rotas que
// apps/inbox de fato consome. Desde catalogo-base-sincronizada (D6), também
// service:connectors só nas rotas de /sync, e subject desconhecido em nada.
public class ServiceScopeAuthorizationTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    [Fact]
    public async Task ServiceToken_OnCreateAgent_ReturnsForbidden()
    {
        var client = ServiceScopedClient();

        var response = await client.PostAsJsonAsync(
            "/agents",
            new { name = "Agente", instructions = "Instrução.", provider = "openai", model = "gpt-5.6-sol" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceToken_OnGetAgentById_IsAuthorized()
    {
        var agentId = await CreateAgentAsync(factory.CreateClient());

        var response = await ServiceScopedClient().GetAsync($"/agents/{agentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ServiceToken_OnA2ASendMessage_IsAuthorized()
    {
        var agentId = await CreateAgentAsync(factory.CreateClient());
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new
            {
                message = new
                {
                    role = "ROLE_USER",
                    parts = new[] { new { text = "Olá" } },
                    messageId = Guid.NewGuid().ToString(),
                },
            },
        };

        var response = await ServiceScopedClient().PostAsJsonAsync($"/agents/{agentId}/a2a", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ServiceToken_OnKnowledgeIndexDiagnostics_ReturnsForbidden()
    {
        var response = await ServiceScopedClient().GetAsync("/knowledge-index/diagnostics");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Tabela de subjects (catalogo-base-sincronizada, D6) -----------------

    private static readonly (string Method, string PathTemplate)[] SyncRoutes =
    [
        ("GET", "/sync/knowledge-bases"),
        ("GET", "/sync/knowledge-bases/{0}/documents"),
        ("PUT", "/sync/knowledge-bases/{0}/documents"),
        ("DELETE", "/sync/knowledge-bases/{0}/documents?externalRef=ref-escopo"),
        ("POST", "/sync/knowledge-bases/{0}/sync-results"),
    ];

    private static HttpRequestMessage SyncRequest((string Method, string PathTemplate) route, Guid knowledgeBaseId)
    {
        var request = new HttpRequestMessage(new HttpMethod(route.Method), string.Format(route.PathTemplate, knowledgeBaseId));
        if (route.Method == "PUT")
        {
            request.Content = JsonContent.Create(new
            {
                externalRef = "ref-escopo", externalVersion = "v1", title = "Doc", sourceType = "markdown", content = "# Texto\n",
            });
        }
        else if (route.Method == "POST")
        {
            request.Content = JsonContent.Create(new { outcome = "Failed", error = new { code = "access-denied" } });
        }

        return request;
    }

    [Fact]
    public async Task ConnectorsToken_IsAuthorizedOnEverySyncRoute()
    {
        var knowledgeBase = await Knowledge.KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var client = Knowledge.KnowledgeSyncTestSeed.CreateConnectorsClient(factory);

        foreach (var route in SyncRoutes)
        {
            var response = await client.SendAsync(SyncRequest(route, knowledgeBase.Id));

            Assert.True(response.IsSuccessStatusCode, $"{route.Method} {route.PathTemplate}: {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task ConnectorsToken_OnOperatorRoutes_ReturnsForbidden()
    {
        var operatorClient = factory.CreateClient();
        var knowledgeBase = await Knowledge.KnowledgeTestClient.CreateBaseAsync(operatorClient, "Base do operador escopo");
        var client = Knowledge.KnowledgeSyncTestSeed.CreateConnectorsClient(factory);

        var list = await client.GetAsync("/knowledge-bases");
        var createDocument = await client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new { title = "Doc", sourceType = "markdown", content = "# Texto\n" });

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createDocument.StatusCode);
        Assert.Equal(0, await Knowledge.KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id));
    }

    [Fact]
    public async Task InboxToken_OnEverySyncRoute_ReturnsForbiddenAndWritesNothing()
    {
        var knowledgeBase = await Knowledge.KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var client = ServiceScopedClient();

        foreach (var route in SyncRoutes)
        {
            var response = await client.SendAsync(SyncRequest(route, knowledgeBase.Id));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var stored = await Knowledge.KnowledgeSyncTestSeed.ReadBaseAsync(factory.Services, knowledgeBase.Id);
        Assert.Null(stored.LastSyncFinishedAt);
        Assert.Equal(0, await Knowledge.KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id));
    }

    [Fact]
    public async Task UnknownSubject_OnOperatorRoute_ReturnsForbidden()
    {
        var client = Knowledge.KnowledgeSyncTestSeed.CreateClientAs(factory, "service:desconhecido");

        var response = await client.GetAsync("/knowledge-bases");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operator_KeepsFullAccess_IncludingSyncRoutes()
    {
        var client = factory.CreateClient();

        var operatorRoute = await client.GetAsync("/knowledge-bases");
        var syncRoute = await client.GetAsync("/sync/knowledge-bases");

        Assert.Equal(HttpStatusCode.OK, operatorRoute.StatusCode);
        Assert.Equal(HttpStatusCode.OK, syncRoute.StatusCode);
    }

    /// <summary>
    /// A composição real (convenção 8): as listas de produção conferidas contra os
    /// endpoints do host que a fixture construiu, e não contra rotas mapeadas no
    /// teste. A lista não vazia é afirmada para o teste não passar por vacuidade.
    /// </summary>
    [Fact]
    public void ProductionServiceRouteLists_MatchTheBuiltHostEndpoints()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        Assert.Equal(7, ServiceScopeAuthorizationHandler.ServiceRoutes.Values.Sum(routes => routes.Count));
        Assert.Empty(ServiceScopeRouteValidation.FindUnmappedRoutes(endpoints, ServiceScopeAuthorizationHandler.ServiceRoutes));
    }

    private HttpClient ServiceScopedClient()
    {
        var client = factory.CreateClient();
        var tokenService = factory.Services.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.Issue(ServiceScopeAuthorizationHandler.InboxSubject, TimeSpan.FromMinutes(5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CreateAgentAsync(HttpClient authenticatedClient)
    {
        var response = await authenticatedClient.PostAsJsonAsync(
            "/agents",
            new { name = "Agente de teste", instructions = "Instrução.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
