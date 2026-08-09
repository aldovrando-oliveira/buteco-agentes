using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

public class ContactSessionResolverTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    [Fact]
    public async Task FindOrCreateSessionAsync_FirstCall_CreatesContactAndSession()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        var session = await ResolveAsync(channelId, externalId);

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.False(string.IsNullOrWhiteSpace(session.ContextId));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking()
            .SingleAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        Assert.Equal(contact.Id, session.ContactId);
    }

    [Fact]
    public async Task FindOrCreateSessionAsync_WithinTimeout_ReusesSessionAndUpdatesLastActivityAt()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        var first = await ResolveAsync(channelId, externalId);
        var second = await ResolveAsync(channelId, externalId);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ContextId, second.ContextId);
        Assert.True(second.LastActivityAt >= first.LastActivityAt);
    }

    [Fact]
    public async Task FindOrCreateSessionAsync_AfterTimeout_CreatesNewSessionForSameContact()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        var first = await ResolveAsync(channelId, externalId);

        // Simula o timeout de inatividade sem IClock/TimeProvider (não
        // existe nenhuma abstração de relógio no projeto) — mesmo mecanismo
        // de ConversationHistoryTests (apps/workers): backdate direto via
        // SQL, não toca o relógio da aplicação (design.md, Decision 3).
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE sessions SET "LastActivityAt" = {DateTimeOffset.UtcNow.AddHours(-2)} WHERE "Id" = {first.Id}""");
        }

        var second = await ResolveAsync(channelId, externalId);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.ContextId, second.ContextId);
        Assert.Equal(first.ContactId, second.ContactId);
    }

    [Fact]
    public async Task FindOrCreateSessionAsync_SameChannelAndExternalId_AlwaysResolvesToSameContact()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        var first = await ResolveAsync(channelId, externalId);
        var second = await ResolveAsync(channelId, externalId);
        var third = await ResolveAsync(channelId, externalId);

        Assert.Equal(first.ContactId, second.ContactId);
        Assert.Equal(first.ContactId, third.ContactId);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contactCount = await dbContext.Contacts
            .CountAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        Assert.Equal(1, contactCount);
    }

    [Fact]
    public async Task FindOrCreateSessionAsync_SameExternalIdDifferentChannel_ResolvesToDistinctContacts()
    {
        var channelIdA = await CreateChannelAsync();
        var channelIdB = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        var sessionA = await ResolveAsync(channelIdA, externalId);
        var sessionB = await ResolveAsync(channelIdB, externalId);

        Assert.NotEqual(sessionA.ContactId, sessionB.ContactId);
    }

    [Fact]
    public async Task FindOrCreateSessionAsync_ConcurrentCallsSamePair_ResolveToSameContactWithoutUnhandledException()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        // Cada chamada usa seu próprio scope/DbContext (mesmo mecanismo de
        // AppDbContext Scoped da aplicação real) para gerar concorrência
        // de verdade contra o Postgres do Testcontainers, exercitando o
        // retry-as-find sobre a violação de unique constraint (design.md,
        // Decision 7).
        var sessions = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ResolveAsync(channelId, externalId)));

        var distinctContactIds = sessions.Select(session => session.ContactId).Distinct().ToList();
        Assert.Single(distinctContactIds);
    }

    private async Task<Session> ResolveAsync(Guid channelId, string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IContactSessionResolver>();
        return await resolver.FindOrCreateSessionAsync(channelId, externalId, CancellationToken.None);
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
