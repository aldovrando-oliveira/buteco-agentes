using System.Net;
using System.Net.Http.Json;
using A2A;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Testing;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MessageEntity = Buteco.Inbox.Messages.Entities.Message;
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
    public async Task ReceiveAsync_WithResponseMessage_PersistsOutboundMessageAsSentAndMarksInboundMessagesCompleted()
    {
        var (sessionId, _, taskId, token) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        var task = BuildAgentTask(taskId, responseText: "Olá! Sua solicitação foi concluída.");
        var response = await PostPushNotificationAsync(client, taskId, token, task);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var messages = await GetMessagesAsync(sessionId);
        var outbound = Assert.Single(messages, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal("Olá! Sua solicitação foi concluída.", outbound.Content);
        Assert.Equal(MessageContentType.Text, outbound.ContentType);
        Assert.Equal(MessageDeliveryStatus.Sent, outbound.DeliveryStatus);
        Assert.Null(outbound.DeliveryFailureReason);

        var inbound = Assert.Single(messages, m => m.Direction == MessageDirection.Inbound);
        Assert.Equal(MessageDispatchStatus.Completed, inbound.DispatchStatus);
    }

    [Fact]
    public async Task ReceiveAsync_SenderThrows_PersistsOutboundMessageAsFailedWithReasonAndDoesNotFailRequest()
    {
        var sender = (TestOutboundMessageSender)factory.Services.GetRequiredKeyedService<IOutboundMessageSender>(ChannelType);
        var (sessionId, _, taskId, token) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        sender.ExceptionToThrow = new InvalidOperationException("Falha simulada no envio ao provedor.");
        try
        {
            var task = BuildAgentTask(taskId, responseText: "Resposta que não consegue ser entregue.");
            var response = await PostPushNotificationAsync(client, taskId, token, task);

            // Falha do sender não derruba a requisição (convenção 4) — só
            // vira estado persistido em Message (design.md, Decisão 4).
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            sender.ExceptionToThrow = null;
        }

        var messages = await GetMessagesAsync(sessionId);
        var outbound = Assert.Single(messages, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal(MessageDeliveryStatus.Failed, outbound.DeliveryStatus);
        Assert.Equal("Falha simulada no envio ao provedor.", outbound.DeliveryFailureReason);
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

    [Fact]
    public async Task ReceiveAsync_WithoutResponseMessage_MarksInboundMessagesCompletedWithoutPersistingOutboundMessage()
    {
        var (sessionId, _, taskId, token) = await SeedDispatchingPendingDispatchAsync();
        var client = factory.CreateClient();

        await PostPushNotificationAsync(client, taskId, token, BuildAgentTask(taskId));

        var messages = await GetMessagesAsync(sessionId);
        Assert.DoesNotContain(messages, m => m.Direction == MessageDirection.Outbound);
        var inbound = Assert.Single(messages, m => m.Direction == MessageDirection.Inbound);
        Assert.Equal(MessageDispatchStatus.Completed, inbound.DispatchStatus);
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

    // Espelha o formato real produzido por AgentExecutionService.ExecuteAsync
    // (apps/workers): a resposta vira um Artifact via AddArtifactAsync,
    // CompleteAsync() é chamado sem mensagem final — Status.Message nunca é
    // preenchido em produção.
    private static AgentTask BuildAgentTask(string taskId, string? responseText = null) => new()
    {
        Id = taskId,
        ContextId = Guid.NewGuid().ToString("N"),
        Status = new TaskStatus
        {
            State = TaskState.Completed,
            Timestamp = DateTimeOffset.UtcNow,
        },
        Artifacts = responseText is null
            ? null
            : [new Artifact { ArtifactId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText(responseText)] }],
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
        var contact = new Contact(channel.Id, externalId, new Dictionary<string, string>(), displayName: null);
        dbContext.Contacts.Add(contact);

        var session = new Session(contact.Id);
        dbContext.Sessions.Add(session);

        var pendingDispatch = new PendingDispatch(session.Id, "Mensagem em voo", DateTimeOffset.UtcNow);
        var token = Guid.NewGuid().ToString("N");
        pendingDispatch.MarkDispatching(token);
        var taskId = Guid.NewGuid().ToString("N");
        pendingDispatch.RegisterTaskId(taskId);
        dbContext.PendingDispatches.Add(pendingDispatch);

        // Correlacionada ao PendingDispatch (design.md, Decisão 6) — sem
        // isso, ReceiveAsync não teria nenhuma Message pra atualizar para
        // Completed.
        dbContext.Messages.Add(MessageEntity.CreateInbound(
            session.Id, "Mensagem em voo", MessageContentType.Text, DateTimeOffset.UtcNow, Guid.NewGuid().ToString(), pendingDispatch.Id));

        await dbContext.SaveChangesAsync();

        return (session.Id, externalId, taskId, token);
    }

    private async Task<List<MessageEntity>> GetMessagesAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Messages.AsNoTracking().Where(m => m.SessionId == sessionId).ToListAsync();
    }
}
