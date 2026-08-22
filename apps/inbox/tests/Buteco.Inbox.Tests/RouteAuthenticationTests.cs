using System.Net;
using System.Net.Http.Json;
using A2A;
using Buteco.Inbox.Channels.Adapters.Testing;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Inbox.Tests;

public class RouteAuthenticationTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private const string ChannelType = "test-channel";

    [Fact]
    public async Task GetChannels_WithoutToken_ReturnsUnauthorized()
    {
        var client = UnauthenticatedClient();

        var response = await client.GetAsync("/channels");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetChannels_WithInvalidToken_ReturnsUnauthorized()
    {
        var client = UnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "token-invalido");

        var response = await client.GetAsync("/channels");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_WithoutToken_StaysAnonymous()
    {
        var response = await UnauthenticatedClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_WithoutToken_StaysAnonymous()
    {
        // Canal inexistente de propósito — o que importa é que a rota
        // processa a requisição (404, decidido pelo handler) em vez de
        // rejeitar por falta de Authorization (401, que seria o middleware
        // novo bloqueando a rota).
        var response = await UnauthenticatedClient().PostAsJsonAsync($"/webhooks/{Guid.NewGuid()}", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PushNotification_WithoutToken_StaysAnonymous()
    {
        var (sessionId, taskId, capabilityToken) = await SeedDispatchingPendingDispatchAsync();
        var client = UnauthenticatedClient();

        var request = new HttpRequestMessage(HttpMethod.Post, PushNotificationEndpoints.RoutePattern)
        {
            Content = JsonContent.Create(BuildAgentTask(taskId)),
        };
        request.Headers.Add(PushNotificationEndpoints.TokenHeaderName, capabilityToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindPendingDispatchAsync(sessionId));
    }

    private HttpClient UnauthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    private static AgentTask BuildAgentTask(string taskId) => new()
    {
        Id = taskId,
        ContextId = Guid.NewGuid().ToString("N"),
        Status = new TaskStatus { State = TaskState.Completed, Timestamp = DateTimeOffset.UtcNow },
    };

    private async Task<PendingDispatch?> FindPendingDispatchAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.PendingDispatches.AsNoTracking().SingleOrDefaultAsync(d => d.SessionId == sessionId);
    }

    private async Task<(Guid SessionId, string TaskId, string Token)> SeedDispatchingPendingDispatchAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<IChannelCredentialCipher>();

        var channel = new Channel(ChannelType, $"Canal {Guid.NewGuid()}", cipher.Encrypt("irrelevante-nesta-fatia"), Guid.NewGuid());
        dbContext.Channels.Add(channel);

        var externalId = $"+5511{Guid.NewGuid():N}"[..15];
        var contact = new Contact(channel.Id, externalId, new Dictionary<string, string>());
        dbContext.Contacts.Add(contact);

        var session = new Session(contact.Id);
        dbContext.Sessions.Add(session);

        var pendingDispatch = new PendingDispatch(session.Id, "Mensagem em voo", DateTimeOffset.UtcNow);
        var token = Guid.NewGuid().ToString("N");
        pendingDispatch.MarkDispatching(token);
        var taskId = Guid.NewGuid().ToString("N");
        pendingDispatch.RegisterTaskId(taskId);
        dbContext.PendingDispatches.Add(pendingDispatch);

        await dbContext.SaveChangesAsync();

        return (session.Id, taskId, token);
    }
}
