using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

// Usa InboxFactoryFixture (janela de debounce default, 10s) de propósito —
// estes testes só verificam o comportamento de bufferização em si (tasks.md
// 6.1), não precisam do DebounceSweepService disparando de verdade dentro
// da janela do teste.
public class InboundMessageOrchestratorTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    [Fact]
    public async Task ReceiveMessageAsync_FirstMessage_CreatesPendingDispatchWithSingleMessage()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        await ReceiveAsync(channelId, externalId, "Olá");

        var pendingDispatch = await FindPendingDispatchAsync(channelId, externalId);

        Assert.NotNull(pendingDispatch);
        Assert.Equal(PendingDispatchStatus.Pending, pendingDispatch.Status);
        Assert.Single(pendingDispatch.Messages);
        Assert.Equal("Olá", pendingDispatch.Messages[0].Text);
    }

    [Fact]
    public async Task ReceiveMessageAsync_SecondMessageBeforeDispatch_AppendsToSamePendingDispatch()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        await ReceiveAsync(channelId, externalId, "Primeira mensagem");
        await ReceiveAsync(channelId, externalId, "Segunda mensagem");

        var pendingDispatches = await AllPendingDispatchesAsync(channelId, externalId);
        Assert.Single(pendingDispatches);

        var pendingDispatch = pendingDispatches[0];
        Assert.Equal(2, pendingDispatch.Messages.Count);
        Assert.Equal("Primeira mensagem", pendingDispatch.Messages[0].Text);
        Assert.Equal("Segunda mensagem", pendingDispatch.Messages[1].Text);
    }

    [Fact]
    public async Task ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        // Mesma corrida (e mesma mitigação) que
        // ContactSessionResolverTests.FindOrCreateSessionAsync_ConcurrentCallsSamePair_...
        // exercita para Contact — aqui é a PendingDispatch Pending por
        // Session que precisa colidir em vez de duplicar (design.md,
        // Riscos).
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => ReceiveAsync(channelId, externalId, $"Mensagem {i}")));

        var pendingDispatches = await AllPendingDispatchesAsync(channelId, externalId);
        Assert.Single(pendingDispatches);
        Assert.Equal(8, pendingDispatches[0].Messages.Count);
    }

    private async Task ReceiveAsync(Guid channelId, string externalId, string text)
    {
        using var scope = factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(channelId, externalId, text, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private async Task<PendingDispatch?> FindPendingDispatchAsync(Guid channelId, string externalId)
    {
        var all = await AllPendingDispatchesAsync(channelId, externalId);
        return all.SingleOrDefault();
    }

    private async Task<List<PendingDispatch>> AllPendingDispatchesAsync(Guid channelId, string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var contact = await dbContext.Contacts.AsNoTracking()
            .SingleAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        var sessionIds = await dbContext.Sessions.AsNoTracking()
            .Where(s => s.ContactId == contact.Id)
            .Select(s => s.Id)
            .ToListAsync();

        return await dbContext.PendingDispatches.AsNoTracking()
            .Where(d => sessionIds.Contains(d.SessionId))
            .ToListAsync();
    }

    private static string UniqueExternalId() => $"+5511{Guid.NewGuid():N}"[..15];

    private async Task<Guid> CreateChannelAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = new Channel(ChannelType.WhatsApp, $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", Guid.NewGuid());
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel.Id;
    }
}
