using System.Net;
using System.Net.Http.Json;
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
