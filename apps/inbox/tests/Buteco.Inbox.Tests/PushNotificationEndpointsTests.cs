using System.Net;
using System.Net.Http.Json;
using A2A;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Inbox.Tests;

// Usa InboxFactoryFixture (não OrchestrationFactoryFixture) — a
// PendingDispatch Dispatching é semeada diretamente no banco, sem depender
// de DebounceSweepService/IA2AClientFactory disparando de verdade.
public class PushNotificationEndpointsTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    [Fact]
    public async Task ReceiveAsync_WithCorrectToken_AcceptsAndRemovesPendingDispatch()
    {
        var (sessionId, taskId, token) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        var response = await PostPushNotificationAsync(client, taskId, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindPendingDispatchAsync(sessionId));
    }

    [Fact]
    public async Task ReceiveAsync_WithoutToken_IsRejectedAndDoesNotAlterPendingDispatch()
    {
        var (sessionId, taskId, _) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(PushNotificationEndpoints.RoutePattern, BuildAgentTask(taskId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(await FindPendingDispatchAsync(sessionId));
    }

    [Fact]
    public async Task ReceiveAsync_WithWrongToken_IsRejectedAndDoesNotAlterPendingDispatch()
    {
        var (sessionId, taskId, _) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        var response = await PostPushNotificationAsync(client, taskId, "token-errado");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(await FindPendingDispatchAsync(sessionId));
    }

    [Fact]
    public async Task ReceiveAsync_ForUnknownTaskId_IsRejected()
    {
        var client = factory.CreateClient();

        var response = await PostPushNotificationAsync(client, Guid.NewGuid().ToString("N"), "qualquer-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostPushNotificationAsync(HttpClient client, string taskId, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PushNotificationEndpoints.RoutePattern)
        {
            Content = JsonContent.Create(BuildAgentTask(taskId)),
        };
        request.Headers.Add(PushNotificationEndpoints.TokenHeaderName, token);
        return client.SendAsync(request);
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

        var channel = new Channel(ChannelType.WhatsApp, $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", Guid.NewGuid());
        dbContext.Channels.Add(channel);

        var contact = new Contact(channel.Id, $"+5511{Guid.NewGuid():N}"[..15]);
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
