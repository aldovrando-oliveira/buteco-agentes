using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.ExternalApplications;

/// <summary>
/// O token de aplicação externa no <c>apps/api</c> (spec <c>route-roles</c>, "O token de aplicação externa
/// é o subject application, sem papel, só na rota A2A"; <c>route-authentication</c>; changes
/// aplicacoes-externas-credencial, D1 e D2, e agentes-por-aplicacao, D2.1). O que a aplicação alcança
/// dentro da rota A2A está em <see cref="ApplicationA2ARouteTests"/>.
/// </summary>
public partial class ExternalApplicationAuthorizationTests(ExternalApplicationsFixture factory) : IClassFixture<ExternalApplicationsFixture>
{
    // (guarda 2.4 da change agentes-por-aplicacao; era a 4.5 da aplicacoes-externas-credencial) "403 em toda
    // rota autenticada, exceto a A2A" (route-roles). O token real de uma aplicação, emitido pelo
    // /connect/token, chama cada rota autenticada mapeada no host, em cada método: todas respondem 403, e
    // nenhuma 401, exceto POST /agents/{id}/a2a, a única da lista de application. Nela, a resposta não é
    // 403 para uma aplicação com o agente liberado; o que ela alcança ali é do ApplicationA2AFilter.
    // Defeito: ("GET", "/agents/{id:guid}") acrescentada à lista de application.
    [Fact]
    public async Task ApplicationToken_OnEveryAuthenticatedRoute_ExceptTheA2A_ReturnsForbidden()
    {
        var application = await factory.CreateAsync();
        var client = factory.WithToken(await factory.AccessTokenAsync(application.ClientId, application.Secret));
        var routes = AuthenticatedRoutes();

        Assert.NotEmpty(routes);
        Assert.Contains(("POST", "/agents/{id}/a2a"), routes);
        var wrong = new List<string>();
        foreach (var (method, pattern) in routes.Where(route => route != ("POST", "/agents/{id}/a2a")))
        {
            var request = new HttpRequestMessage(new HttpMethod(method), RouteParameter().Replace(pattern, Guid.NewGuid().ToString()));
            if (method is "POST" or "PUT")
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                wrong.Add($"{method} {pattern}: {(int)response.StatusCode}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));

        var agentId = await AgentAsync("Agente liberado");
        var admin = await factory.AdminClientAsync();
        (await admin.PostAsJsonAsync($"/applications/{application.Id}/agents", new { agenteId = agentId, operacoes = new[] { "consultar-task" } }))
            .EnsureSuccessStatusCode();
        var a2a = await client.PostAsJsonAsync($"/agents/{agentId}/a2a", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "GetTask",
            @params = new { id = Guid.NewGuid().ToString("N") },
        });
        Assert.NotEqual(HttpStatusCode.Forbidden, a2a.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, a2a.StatusCode);
    }

    // "A2A sem vínculo recusada sem publicar job" (route-roles). Até a #186 a resposta era 403, com a lista
    // de application vazia; desde a agentes-por-aplicacao é o 404 do agente sem vínculo, e continua sem job.
    [Fact]
    public async Task ApplicationToken_WithoutALink_OnTheA2ARoute_IsNotFound_AndPublishesNoJob()
    {
        var admin = await factory.AdminAsync();
        var created = await factory.As(admin).PostAsJsonAsync(
            "/agents/",
            new { name = "Agente da aplicação", instructions = "Responda.", provider = "openai", model = "gpt-5.6-sol" });
        created.EnsureSuccessStatusCode();
        var agentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var application = await factory.CreateAsync();
        var client = factory.WithToken(await factory.AccessTokenAsync(application.ClientId, application.Secret));

        var response = await client.PostAsJsonAsync($"/agents/{agentId}/a2a", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new { message = new { role = "ROLE_USER", parts = new[] { new { text = "Olá" } }, messageId = Guid.NewGuid().ToString("N") } },
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(factory.TaskJobPublisher.PublishedMessages, message => message.AgentId == agentId);
    }

    private async Task<Guid> AgentAsync(string name)
    {
        var created = await (await factory.AdminClientAsync()).PostAsJsonAsync(
            "/agents/",
            new { name, instructions = "Responda.", provider = "openai", model = "gpt-5.6-sol" });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    // (guarda 4.3) "buteco_app forjado num cliente da tabela fechada" (route-roles; Q1). O cliente da tabela
    // continua com o subject dele: passa na rota do service:inbox e não ganha a do painel. Defeito: o
    // apps/api mapeando application só pelo claim, antes da tabela e sem conferir o client_id.
    [Fact]
    public async Task AForgedApplicationClaim_OnAClosedTableClient_KeepsItsSubject()
    {
        var admin = await factory.AdminAsync();
        var created = await factory.As(admin).PostAsJsonAsync(
            "/agents/",
            new { name = "Agente do inbox", instructions = "Responda.", provider = "openai", model = "gpt-5.6-sol" });
        created.EnsureSuccessStatusCode();
        var agentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var forged = TestIssuerTokens.ForClient(
            factory.Services, IdentityClients.InboxServiceClientId, IdentityClients.InboxServiceClientId, application: Guid.NewGuid().ToString());

        var inboxRoute = await factory.WithToken(forged).GetAsync($"/agents/{agentId}");
        var panelRoute = await factory.WithToken(forged).GetAsync("/agents/");

        Assert.Equal(HttpStatusCode.OK, inboxRoute.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, panelRoute.StatusCode);
    }

    // "Cliente fora do prefixo sem buteco_app" (route-roles): sem o claim do emissor, o cliente fora da
    // tabela não vira application; autentica sem subject e recebe 403.
    [Fact]
    public async Task AClientOutsideTheTable_WithoutTheApplicationClaim_IsForbidden()
    {
        var token = TestIssuerTokens.ForClient(factory.Services, "teste-sem-cadastro", "teste-sem-cadastro");

        var response = await factory.WithToken(token).GetAsync("/agents/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // "Token de aplicação externa recusado em rota do painel" (route-authentication).
    [Fact]
    public async Task ApplicationToken_OnAPanelRoute_IsForbidden()
    {
        var application = await factory.CreateAsync();
        var client = factory.WithToken(await factory.AccessTokenAsync(application.ClientId, application.Secret));

        var response = await client.GetAsync("/knowledge-bases");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private IReadOnlyList<(string Method, string Pattern)> AuthenticatedRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (method.ToUpperInvariant(), endpoint.RoutePattern.RawText ?? string.Empty)))
            .Distinct()
            .OrderBy(route => route.Item2, StringComparer.Ordinal)
            .ThenBy(route => route.Item1, StringComparer.Ordinal)
            .ToList();

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
