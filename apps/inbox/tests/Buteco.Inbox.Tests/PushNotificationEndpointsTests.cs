using System.Net;
using System.Net.Http.Json;
using A2A;
using Buteco.Inbox.Channels.Adapters;
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

// Usa InboxFactoryFixture (não OrchestrationFactoryFixture) — a
// PendingDispatch Dispatching é semeada diretamente no banco, sem depender
// de DebounceSweepService/IA2AClientFactory disparando de verdade.
public class PushNotificationEndpointsTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private const string ChannelType = "test-channel";

    [Fact]
    public async Task ReceiveAsync_WithCorrectToken_AcceptsAndRemovesPendingDispatch()
    {
        var (sessionId, _, taskId, token) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        var response = await PostPushNotificationAsync(client, taskId, token, BuildAgentTask(taskId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindPendingDispatchAsync(sessionId));
    }

    [Fact]
    public async Task ReceiveAsync_WithoutToken_IsRejectedAndDoesNotAlterPendingDispatch()
    {
        var (sessionId, _, taskId, _) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(PushNotificationEndpoints.RoutePattern, BuildAgentTask(taskId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(await FindPendingDispatchAsync(sessionId));
    }

    [Fact]
    public async Task ReceiveAsync_WithWrongToken_IsRejectedAndDoesNotAlterPendingDispatch()
    {
        var (sessionId, _, taskId, _) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        var response = await PostPushNotificationAsync(client, taskId, "token-errado", BuildAgentTask(taskId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(await FindPendingDispatchAsync(sessionId));
    }

    [Fact]
    public async Task ReceiveAsync_ForUnknownTaskId_IsRejected()
    {
        var client = factory.CreateClient();
        var taskId = Guid.NewGuid().ToString("N");

        var response = await PostPushNotificationAsync(client, taskId, "qualquer-token", BuildAgentTask(taskId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReceiveAsync_WithResponseMessage_InvokesRegisteredSenderWithDecryptedCredentialAndText()
    {
        const string plaintextCredential = "credencial-do-canal-em-claro";
        var (sessionId, externalId, taskId, token) = await SeedDispatchingPendingDispatchAsync(plaintextCredential);
        var client = factory.CreateClient();

        var task = BuildAgentTask(taskId, responseText: "Olá! Sua solicitação foi concluída.");
        var response = await PostPushNotificationAsync(client, taskId, token, task);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindPendingDispatchAsync(sessionId));

        var sender = (TestOutboundMessageSender)factory.Services.GetRequiredKeyedService<IOutboundMessageSender>(ChannelType);
        var captured = Assert.Single(sender.CapturedMessages, message => message.ContactExternalId == externalId);
        Assert.Equal(plaintextCredential, captured.DecryptedCredential);
        Assert.Equal("Olá! Sua solicitação foi concluída.", captured.ResponseText);
    }

    [Fact]
    public async Task ReceiveAsync_WithoutResponseMessage_DoesNotInvokeSender()
    {
        var (sessionId, externalId, taskId, token) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        var response = await PostPushNotificationAsync(client, taskId, token, BuildAgentTask(taskId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindPendingDispatchAsync(sessionId));

        var sender = (TestOutboundMessageSender)factory.Services.GetRequiredKeyedService<IOutboundMessageSender>(ChannelType);
        Assert.DoesNotContain(sender.CapturedMessages, message => message.ContactExternalId == externalId);
    }

    private static Task<HttpResponseMessage> PostPushNotificationAsync(HttpClient client, string taskId, string token, AgentTask task)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PushNotificationEndpoints.RoutePattern)
        {
            Content = JsonContent.Create(task),
        };
        request.Headers.Add(PushNotificationEndpoints.TokenHeaderName, token);
        return client.SendAsync(request);
    }

    private static AgentTask BuildAgentTask(string taskId, string? responseText = null) => new()
    {
        Id = taskId,
        ContextId = Guid.NewGuid().ToString("N"),
        Status = new TaskStatus
        {
            State = TaskState.Completed,
            Timestamp = DateTimeOffset.UtcNow,
            Message = responseText is null
                ? null
                : new Message
                {
                    Role = Role.Agent,
                    Parts = [Part.FromText(responseText)],
                    MessageId = Guid.NewGuid().ToString("N"),
                },
        },
    };

    private async Task<PendingDispatch?> FindPendingDispatchAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.PendingDispatches.AsNoTracking().SingleOrDefaultAsync(d => d.SessionId == sessionId);
    }

    private async Task<(Guid SessionId, string ExternalId, string TaskId, string Token)> SeedDispatchingPendingDispatchAsync(string plaintextCredential = "irrelevante-nesta-fatia")
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<IChannelCredentialCipher>();

        var channel = new Channel(ChannelType, $"Canal {Guid.NewGuid()}", cipher.Encrypt(plaintextCredential), Guid.NewGuid());
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

        return (session.Id, externalId, taskId, token);
    }
}
