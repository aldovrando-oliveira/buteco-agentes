using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Buteco.Inbox.Auth;
using Buteco.Inbox.Channels.Requests;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Buteco.Inbox.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Buteco.Inbox.Tests;

// Tabela de subjects do apps/inbox (change inbox-restricao-de-subject, D1/D6):
// só "operator" é autorizado nas rotas autenticadas; qualquer outro subject,
// com token validamente assinado pela mesma chave, recebe 403. As rotas
// anônimas não mudam com um token de serviço presente.
public partial class SubjectAuthorizationTests(InboxFactoryFixture factory, ITestOutputHelper output) : IClassFixture<InboxFactoryFixture>
{
    private const string ConnectorsSubject = "service:connectors";

    [Theory]
    [InlineData(ConnectorsSubject)]
    [InlineData(ServiceTokenDelegatingHandler.ServiceSubject)]
    [InlineData("service:desconhecido")]
    [InlineData("Operator")]
    public async Task GetChannels_WithSubjectOtherThanOperator_ReturnsForbidden(string subject)
    {
        var response = await ClientAs(subject).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EveryAuthenticatedRoute_WithServiceSubject_ReturnsForbidden()
    {
        var routes = AuthenticatedRoutes();
        AssertSweepFoundTheOperatorRoutes(routes);

        var client = ClientAs(ConnectorsSubject);
        var notForbidden = new List<string>();
        foreach (var (method, pattern) in routes)
        {
            var response = await client.SendAsync(BuildRequest(method, pattern));
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                notForbidden.Add($"{method} {pattern} → {(int)response.StatusCode}");
            }
        }

        AssertNoneListed(notForbidden, routes.Count, "não responderam 403 para service:connectors");
    }

    // Par da varredura acima: sem ele, um 403 vindo de outra causa (uma rota
    // que recusasse qualquer um) passaria por recusa de subject.
    [Fact]
    public async Task EveryAuthenticatedRoute_WithOperator_IsNeitherUnauthorizedNorForbidden()
    {
        var routes = AuthenticatedRoutes();
        AssertSweepFoundTheOperatorRoutes(routes);

        var client = factory.CreateClient();
        var refused = new List<string>();
        foreach (var (method, pattern) in routes)
        {
            var response = await client.SendAsync(BuildRequest(method, pattern));
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                refused.Add($"{method} {pattern} → {(int)response.StatusCode}");
            }
        }

        AssertNoneListed(refused, routes.Count, "recusaram o operador");
    }

    [Fact]
    public async Task CreateChannel_WithServiceSubject_ReturnsForbiddenAndCreatesNothing()
    {
        var agentId = Guid.NewGuid();
        factory.AgentApiHandler.ExistingAgentIds.Add(agentId);
        var countBefore = await CountChannelsAsync();

        var response = await ClientAs(ConnectorsSubject).PostAsJsonAsync(
            "/channels",
            new CreateChannelRequest("test-channel", "Canal Recusado", "s3cr3t-token", agentId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(countBefore, await CountChannelsAsync());
    }

    [Fact]
    public async Task Webhook_WithServiceSubject_IsDecidedByTheRoute()
    {
        // Canal inexistente: o 404 é da rota, e prova que a política não a alcançou.
        var response = await ClientAs(ConnectorsSubject).PostAsJsonAsync($"/webhooks/{Guid.NewGuid()}", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_WithServiceSubject_StaysAnonymous()
    {
        var response = await ClientAs(ServiceTokenDelegatingHandler.ServiceSubject).GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PushNotification_WithServiceSubjectAndNoCapabilityToken_IsDecidedByTheRoute()
    {
        // Sem o X-A2A-Notification-Token, a própria rota responde 401. Um 403
        // seria a política de subject alcançando uma rota anônima.
        var response = await ClientAs(ConnectorsSubject).PostAsJsonAsync(
            PushNotificationEndpoints.RoutePattern,
            new { id = "task-sem-capacidade", contextId = "ctx", status = new { state = "TASK_STATE_COMPLETED" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient ClientAs(string subject)
    {
        var client = factory.CreateClient();
        var tokenService = factory.Services.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.Issue(subject, TimeSpan.FromMinutes(5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Lidas do host construído, não de uma lista escrita no teste: uma rota nova
    // entra na varredura sem ninguém lembrar dela.
    private List<(string Method, string Pattern)> AuthenticatedRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (method, endpoint.RoutePattern.RawText!)))
            .ToList();

    // Contra a vacuidade: uma varredura vazia passaria com e sem a correção.
    private void AssertSweepFoundTheOperatorRoutes(List<(string Method, string Pattern)> routes)
    {
        output.WriteLine($"Varredura: {routes.Count} pares (método, rota) autenticados.");
        Assert.NotEmpty(routes);
        Assert.Contains(("GET", "/channels/"), routes);
    }

    // A falha lista todos os pares, sem o truncamento que Assert.Empty aplica.
    private static void AssertNoneListed(List<string> offenders, int total, string what)
    {
        if (offenders.Count > 0)
        {
            Assert.Fail($"{offenders.Count} de {total} rotas {what}:\n{string.Join("\n", offenders)}");
        }
    }

    private static HttpRequestMessage BuildRequest(string method, string pattern)
    {
        var path = RouteParameter().Replace(pattern, _ => Guid.NewGuid().ToString());
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(new { });
        }

        return request;
    }

    private async Task<int> CountChannelsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Channels.CountAsync();
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
