extern alias ApiAssembly;
extern alias InboxAssembly;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InboxOrchestratorRoundTrip.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using InboxA2AClientFactory = InboxAssembly::Buteco.Inbox.Orchestration.A2AClientFactory;
using InboxAgentReferenceValidator = InboxAssembly::Buteco.Inbox.Agents.AgentReferenceValidator;
using InboxAppDbContext = InboxAssembly::Buteco.Inbox.Infrastructure.AppDbContext;
using InboxChannel = InboxAssembly::Buteco.Inbox.Channels.Entities.Channel;
using InboxInboundMessageOrchestrator = InboxAssembly::Buteco.Inbox.Orchestration.IInboundMessageOrchestrator;
using InboxMessageContentType = InboxAssembly::Buteco.Inbox.Messages.Entities.MessageContentType;
using InboxProgram = InboxAssembly::Program;
using InboxServiceAccessTokenSource = InboxAssembly::Buteco.Inbox.Auth.ServiceAccessTokenSource;

namespace InboxOrchestratorRoundTrip.Tests;

/// <summary>
/// Acordo do token de serviço do <c>apps/inbox</c> com o emissor real do <c>apps/api</c>
/// (spec <c>inbox-service-authentication</c>; design.md da change servicos-token-emissor, D9;
/// convenção 11): o <c>apps/inbox</c> real pede o token ao <c>/connect/token</c> do
/// <c>apps/api</c> real com o segredo do catálogo, e o usa nos dois fluxos de serviço, o
/// despacho A2A (S2) e a validação de agente no cadastro de canal (S1). Com o segredo
/// divergente, os dois fluxos falham como falham com o <c>apps/api</c> fora do ar.
/// </summary>
public class ServiceTokenRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    private const string DivergentSecret = "Ls4HQCkzM8pbxzO1QXuZ+0H7r066hkSzPWv7KtfHuzM=";

    [Fact]
    public async Task S2_AChannelMessageReachesTheAgent_WithTheIssuerToken_AndOneTokenRequest()
    {
        var agentId = await CreateAgentAsync();
        var (channelId, externalId) = await InsertChannelAsync(fixture.InboxFactory, agentId);

        await ReceiveAsync(fixture.InboxFactory, channelId, externalId);
        await PollAsync(
            async () => !await HasPendingDispatchAsync(fixture.InboxFactory, channelId),
            "o despacho não terminou",
            TimeSpan.FromSeconds(20));

        var calls = fixture.InboxToApi.ToList();
        var a2a = calls.Where(call => call.Client == InboxA2AClientFactory.HttpClientName).ToList();
        Assert.NotEmpty(a2a);
        Assert.All(a2a, call =>
        {
            Assert.NotEqual(401, call.Status);
            Assert.NotEqual(403, call.Status);
            AssertInboxServiceToken(call.Token);
        });
        Assert.Single(calls, call => call.Client == InboxServiceAccessTokenSource.HttpClientName);
    }

    [Fact]
    public async Task S1_TheChannelRegistration_ValidatesTheAgentAgainstTheRealApi_WithTheIssuerToken()
    {
        var agentId = await CreateAgentAsync();
        var inbox = await OperatorInboxClientAsync(fixture.InboxFactory);

        var created = await PollAsync(
            () => inbox.PostAsJsonAsync("/channels", Channel(agentId)),
            response => response.StatusCode != HttpStatusCode.ServiceUnavailable);
        var unknown = await inbox.PostAsJsonAsync("/channels", Channel(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var validations = fixture.InboxToApi.Where(call => call.Client == InboxAgentReferenceValidator.HttpClientName).ToList();
        Assert.Contains(validations, call => call.Path == $"/agents/{agentId}" && call.Status == 200);
        Assert.Contains(validations, call => call.Status == 404);
        Assert.All(validations, call => AssertInboxServiceToken(call.Token));
    }

    [Fact]
    public async Task ADivergentSecret_IsInvalidClient_AndBothFlowsFailAsTheApiDown()
    {
        var agentId = await CreateAgentAsync();
        var calls = new ConcurrentQueue<ServiceCall>();
        var logs = new ConcurrentQueue<(LogLevel Level, string Message)>();
        var divergent = await fixture.BuildExtraInboxAsync(DivergentSecret, calls, logs);

        // S1: o cadastro de canal responde a falha de comunicação com o apps/api.
        var inbox = await OperatorInboxClientAsync(divergent);
        var registration = await PollAsync(
            () => inbox.PostAsJsonAsync("/channels", Channel(agentId)),
            response => response.StatusCode != HttpStatusCode.ServiceUnavailable);
        Assert.Equal(HttpStatusCode.BadGateway, registration.StatusCode);
        await using (var scope = divergent.Services.CreateAsyncScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<InboxAppDbContext>().Channels.AnyAsync());
        }

        // S2: o despacho segue o caminho de falha de transporte, tentativa a tentativa, até o
        // limite de Debounce:MaxDispatchAttempts da fixture, como com o apps/api fora do ar.
        var (channelId, externalId) = await InsertChannelAsync(divergent, agentId);
        await ReceiveAsync(divergent, channelId, externalId);
        await PollAsync(
            () => Task.FromResult(logs.Any(entry => entry.Level == LogLevel.Error && entry.Message.Contains("perdida após 3 tentativas", StringComparison.Ordinal))),
            "o despacho não esgotou as tentativas de transporte",
            TimeSpan.FromSeconds(20));

        Assert.Equal(2, logs.Count(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Falha de transporte", StringComparison.Ordinal)));
        Assert.Contains(logs, entry => entry.Level == LogLevel.Error && entry.Message.Contains("invalid_client", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, entry => entry.Message.Contains("rejeitado no nível de protocolo", StringComparison.Ordinal));

        // Nenhuma chamada passou do pedido de token: nada chegou às rotas do apps/api.
        var all = calls.ToList();
        Assert.NotEmpty(all);
        Assert.All(all, call =>
        {
            Assert.Equal(InboxServiceAccessTokenSource.HttpClientName, call.Client);
            // O OpenIddict responde invalid_client com 401.
            Assert.Equal(401, call.Status);
        });
    }

    private static void AssertInboxServiceToken(string? token)
    {
        Assert.NotNull(token);
        var jwt = new JsonWebToken(token);
        Assert.Equal("at+jwt", jwt.Typ);
        Assert.Equal("buteco-servico-inbox", jwt.GetPayloadValue<string>("client_id"));
        Assert.Equal(["buteco-api"], jwt.Audiences);
    }

    private static object Channel(Guid agentId) =>
        new { channelType = "test-channel", name = $"Canal do acordo {Guid.NewGuid():N}", credential = "credencial-do-acordo", agentId };

    private async Task<HttpClient> OperatorInboxClientAsync(WebApplicationFactory<InboxProgram> inbox)
    {
        var client = inbox.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await fixture.UserAccessTokenAsync("openid buteco-inbox"));
        return client;
    }

    private async Task<Guid> CreateAgentAsync()
    {
        var client = fixture.ApiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await fixture.UserAccessTokenAsync());

        var response = await client.PostAsJsonAsync(
            "/agents",
            new { name = "Agente do acordo de serviço", instructions = "Responda com simpatia.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    // Direto no banco, como RoundTripTests.CreateChannelAsync: o S1 é provado no cadastro.
    private static async Task<(Guid ChannelId, string ExternalId)> InsertChannelAsync(WebApplicationFactory<InboxProgram> inbox, Guid agentId)
    {
        await using var scope = inbox.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxAppDbContext>();
        var channel = new InboxChannel("test-channel", $"Canal do acordo {Guid.NewGuid()}", "irrelevante", agentId);
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return (channel.Id, $"+5511{Guid.NewGuid():N}"[..15]);
    }

    private static async Task ReceiveAsync(WebApplicationFactory<InboxProgram> inbox, Guid channelId, string externalId)
    {
        await using var scope = inbox.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InboxInboundMessageOrchestrator>().ReceiveMessageAsync(
            channelId,
            externalId,
            "Olá, preciso de ajuda",
            InboxMessageContentType.Text,
            Guid.NewGuid().ToString(),
            displayName: null,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            CancellationToken.None);
    }

    private static async Task<bool> HasPendingDispatchAsync(WebApplicationFactory<InboxProgram> inbox, Guid channelId)
    {
        await using var scope = inbox.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxAppDbContext>();
        return await (
            from dispatch in dbContext.PendingDispatches
            join session in dbContext.Sessions on dispatch.SessionId equals session.Id
            join contact in dbContext.Contacts on session.ContactId equals contact.Id
            where contact.ChannelId == channelId
            select dispatch).AnyAsync();
    }

    private static async Task PollAsync(Func<Task<bool>> done, string failure, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await done())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(failure);
            }

            await Task.Delay(100);
        }
    }

    private static async Task<HttpResponseMessage> PollAsync(Func<Task<HttpResponseMessage>> send, Func<HttpResponseMessage, bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var response = await send();
            if (done(response) || DateTime.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(200);
        }
    }
}
