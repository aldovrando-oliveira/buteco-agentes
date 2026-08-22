using System.Net;
using System.Net.Http.Json;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Contacts.Responses;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

public class ChannelSessionEndpointsTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetChannelSessions_ChannelWithSessions_ReturnsOrderedByLastActivityWithDisplayNameAndPreview()
    {
        var channelId = await CreateChannelAsync();
        var externalIdOld = UniqueExternalId();
        var externalIdRecent = UniqueExternalId();

        await ResolveAsync(channelId, externalIdOld, displayName: "Contato Antigo");
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        var sessionIdOld = await ReceiveAsync(channelId, externalIdOld, "Mensagem antiga");

        await Task.Delay(TimeSpan.FromMilliseconds(20));
        await ResolveAsync(channelId, externalIdRecent, displayName: "Contato Recente");
        var sessionIdRecent = await ReceiveAsync(channelId, externalIdRecent, "Mensagem recente");

        var response = await _client.GetAsync($"/channels/{channelId}/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessions = await response.Content.ReadFromJsonAsync<List<ChannelSessionResponse>>();
        Assert.NotNull(sessions);

        var indexOfRecent = sessions!.FindIndex(s => s.SessionId == sessionIdRecent);
        var indexOfOld = sessions.FindIndex(s => s.SessionId == sessionIdOld);
        Assert.True(indexOfRecent < indexOfOld, "sessão mais recente deve vir antes da mais antiga");

        var recent = sessions[indexOfRecent];
        Assert.Equal("Contato Recente", recent.ContactDisplayName);
        Assert.Equal(externalIdRecent, recent.ContactExternalId);
        Assert.NotNull(recent.LastMessage);
        Assert.Equal("Mensagem recente", recent.LastMessage!.Content);
        Assert.Equal(MessageDirection.Inbound, recent.LastMessage.Direction);
    }

    [Fact]
    public async Task GetChannelSessions_ChannelWithoutSessions_ReturnsEmptyList()
    {
        var channelId = await CreateChannelAsync();

        var response = await _client.GetAsync($"/channels/{channelId}/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessions = await response.Content.ReadFromJsonAsync<List<ChannelSessionResponse>>();
        Assert.NotNull(sessions);
        Assert.Empty(sessions!);
    }

    [Fact]
    public async Task GetChannelSessions_UnknownChannel_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/channels/{Guid.NewGuid()}/sessions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task ResolveAsync(Guid channelId, string externalId, string? displayName)
    {
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IContactSessionResolver>();
        await resolver.FindOrCreateSessionAsync(channelId, externalId, new Dictionary<string, string>(), displayName, CancellationToken.None);
    }

    private async Task<Guid> ReceiveAsync(Guid channelId, string externalId, string text)
    {
        using var scope = factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channelId,
            externalId,
            text,
            MessageContentType.Text,
            Guid.NewGuid().ToString(),
            displayName: null,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.SingleAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        var session = await dbContext.Sessions.SingleAsync(s => s.ContactId == contact.Id);
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
