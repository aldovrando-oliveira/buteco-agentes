using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Messages.Responses;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

public class MessagePersistenceTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetSessionMessages_SessionWithMessages_ReturnsInChronologicalOrder()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var sessionId = await ResolveSessionIdAsync(channelId, externalId);

        await ReceiveAsync(channelId, externalId, "Primeira");
        await ReceiveAsync(channelId, externalId, "Segunda");

        var response = await _client.GetAsync($"/sessions/{sessionId}/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var messages = await response.Content.ReadFromJsonAsync<List<MessageResponse>>();
        Assert.NotNull(messages);
        Assert.Equal(2, messages!.Count);
        Assert.Equal("Primeira", messages[0].Content);
        Assert.Equal("Segunda", messages[1].Content);
        Assert.True(messages[0].OccurredAt <= messages[1].OccurredAt);
        Assert.All(messages, m => Assert.Equal(MessageDirection.Inbound, m.Direction));
    }

    [Fact]
    public async Task GetSessionMessages_SessionWithoutMessages_ReturnsEmptyList()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var sessionId = await ResolveSessionIdAsync(channelId, externalId);

        var response = await _client.GetAsync($"/sessions/{sessionId}/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var messages = await response.Content.ReadFromJsonAsync<List<MessageResponse>>();
        Assert.NotNull(messages);
        Assert.Empty(messages!);
    }

    [Fact]
    public async Task GetSessionMessages_UnknownSession_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/sessions/{Guid.NewGuid()}/messages");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReceiveMessageAsync_SameExternalMessageIdTwice_DoesNotDuplicateMessageInResponse()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var sessionId = await ResolveSessionIdAsync(channelId, externalId);
        var externalMessageId = Guid.NewGuid().ToString();

        await ReceiveAsync(channelId, externalId, "Mensagem original", externalMessageId);
        await ReceiveAsync(channelId, externalId, "Reentrega do mesmo webhook", externalMessageId);

        var response = await _client.GetAsync($"/sessions/{sessionId}/messages");
        var messages = await response.Content.ReadFromJsonAsync<List<MessageResponse>>();
        Assert.NotNull(messages);
        var message = Assert.Single(messages!);
        Assert.Equal("Mensagem original", message.Content);
        Assert.Equal(externalMessageId, message.ExternalId);
    }

    // inbox-enums-json-string, tasks.md 2.1: lê a resposta como JSON bruto
    // (não ReadFromJsonAsync<MessageResponse>) porque round-trip pelo mesmo
    // tipo C# passaria igual com o enum serializado como inteiro ou como
    // string — só JsonDocument expõe o formato de fio real. Uma sessão com
    // mensagem de entrada e de saída na mesma resposta é necessária porque
    // DeliveryStatus é sempre nulo em mensagem de entrada e DispatchStatus é
    // sempre nulo em mensagem de saída.
    [Fact]
    public async Task GetSessionMessages_InboundAndOutboundMessages_SerializesEnumsAsStrings()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var sessionId = await ResolveSessionIdAsync(channelId, externalId);

        await ReceiveAsync(channelId, externalId, "Mensagem de entrada");
        await FailInboundDispatchAsync(sessionId);
        await CreateOutboundAsync(sessionId, "Mensagem de saída", MessageDeliveryStatus.Sent, deliveryFailureReason: null);

        var response = await _client.GetAsync($"/sessions/{sessionId}/messages");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var messages = document.RootElement.EnumerateArray().ToList();

        var inbound = messages.Single(m => m.GetProperty("direction").GetString() == "Inbound");
        Assert.Equal("Inbound", inbound.GetProperty("direction").GetString());
        Assert.Equal("Text", inbound.GetProperty("contentType").GetString());
        Assert.Equal("Failed", inbound.GetProperty("dispatchStatus").GetString());

        var outbound = messages.Single(m => m.GetProperty("direction").GetString() == "Outbound");
        Assert.Equal("Outbound", outbound.GetProperty("direction").GetString());
        Assert.Equal("Sent", outbound.GetProperty("deliveryStatus").GetString());
    }

    private async Task FailInboundDispatchAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await dbContext.Messages.SingleAsync(m => m.SessionId == sessionId && m.Direction == MessageDirection.Inbound);
        message.UpdateDispatchStatus(MessageDispatchStatus.Failed);
        await dbContext.SaveChangesAsync();
    }

    private async Task CreateOutboundAsync(Guid sessionId, string text, MessageDeliveryStatus deliveryStatus, string? deliveryFailureReason)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Messages.Add(Message.CreateOutbound(sessionId, text, DateTimeOffset.UtcNow, deliveryStatus, deliveryFailureReason));
        await dbContext.SaveChangesAsync();
    }

    private async Task ReceiveAsync(Guid channelId, string externalId, string text, string? externalMessageId = null)
    {
        using var scope = factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channelId,
            externalId,
            text,
            MessageContentType.Text,
            externalMessageId ?? Guid.NewGuid().ToString(),
            displayName: null,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            CancellationToken.None);
    }

    private async Task<Guid> ResolveSessionIdAsync(Guid channelId, string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IContactSessionResolver>();
        var session = await resolver.FindOrCreateSessionAsync(channelId, externalId, new Dictionary<string, string>(), displayName: null, CancellationToken.None);
        return session.Id;
    }

    private static string UniqueExternalId() => $"+5511{Guid.NewGuid():N}"[..15];

    private async Task<Guid> CreateChannelAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", Guid.NewGuid());
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel.Id;
    }
}
