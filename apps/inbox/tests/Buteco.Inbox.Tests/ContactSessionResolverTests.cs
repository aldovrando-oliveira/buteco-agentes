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
    public async Task FindOrCreateSessionAsync_FirstCall_PersistsMetadataOnContact()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var metadata = new Dictionary<string, string> { ["phone"] = "5511999999999" };

        var session = await ResolveAsync(channelId, externalId, metadata);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.Id == session.ContactId);
        Assert.Equal(metadata, contact.Metadata);
    }

    [Fact]
    public async Task FindOrCreateSessionAsync_SubsequentCall_DoesNotOverwritePersistedMetadata()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var originalMetadata = new Dictionary<string, string> { ["phone"] = "5511999999999" };

        var first = await ResolveAsync(channelId, externalId, originalMetadata);
        var second = await ResolveAsync(channelId, externalId, new Dictionary<string, string> { ["phone"] = "5511888888888" });

        Assert.Equal(first.ContactId, second.ContactId);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.Id == second.ContactId);
        Assert.Equal(originalMetadata, contact.Metadata);
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

    [Fact]
    public async Task FindOrCreateSessionAsync_FirstCall_PersistsDisplayName()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        var session = await ResolveAsync(channelId, externalId, new Dictionary<string, string>(), displayName: "Maria");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.Id == session.ContactId);
        Assert.Equal("Maria", contact.DisplayName);
    }

    [Fact]
    public async Task FindOrCreateSessionAsync_FirstCall_WithoutDisplayName_PersistsNull()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        var session = await ResolveAsync(channelId, externalId);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.Id == session.ContactId);
        Assert.Null(contact.DisplayName);
    }

    [Fact]
    public async Task FindOrCreateSessionAsync_SubsequentCall_UpdatesDisplayNameToNewValue()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        var first = await ResolveAsync(channelId, externalId, new Dictionary<string, string>(), displayName: "Maria");
        await ResolveAsync(channelId, externalId, new Dictionary<string, string>(), displayName: "Maria Silva");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.Id == first.ContactId);
        Assert.Equal("Maria Silva", contact.DisplayName);
    }

    [Fact]
    public async Task FindOrCreateSessionAsync_SubsequentCallWithoutDisplayName_DoesNotOverwritePersistedValue()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        var first = await ResolveAsync(channelId, externalId, new Dictionary<string, string>(), displayName: "Maria");
        await ResolveAsync(channelId, externalId, new Dictionary<string, string>(), displayName: null);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.Id == first.ContactId);
        Assert.Equal("Maria", contact.DisplayName);
    }

    private Task<Session> ResolveAsync(Guid channelId, string externalId) =>
        ResolveAsync(channelId, externalId, new Dictionary<string, string>());

    private Task<Session> ResolveAsync(Guid channelId, string externalId, IReadOnlyDictionary<string, string> contactMetadata) =>
        ResolveAsync(channelId, externalId, contactMetadata, displayName: null);

    private async Task<Session> ResolveAsync(
        Guid channelId, string externalId, IReadOnlyDictionary<string, string> contactMetadata, string? displayName)
    {
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IContactSessionResolver>();
        return await resolver.FindOrCreateSessionAsync(channelId, externalId, contactMetadata, displayName, CancellationToken.None);
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
