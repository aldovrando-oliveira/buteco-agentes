using System.Net;
using System.Net.Http.Json;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Contacts.Queries.ListContacts;
using Buteco.Inbox.Contacts.Responses;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

public class ContactEndpointsTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ListContacts_NoContactsInIsolatedDatabase_ReturnsEmptyList()
    {
        // Banco isolado em memória (mesmo mecanismo de
        // CreateChannelCommandHandlerTests), não o Postgres compartilhado
        // pelo resto da classe via InboxFactoryFixture — o fixture é
        // reutilizado por todos os [Fact] desta classe (IClassFixture), sem
        // ordem de execução garantida entre eles, então "sem contatos" só é
        // determinístico com um banco próprio.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new AppDbContext(options);
        var handler = new ListContactsQueryHandler(dbContext);

        var contacts = await handler.Handle(new ListContactsQuery(), CancellationToken.None);

        Assert.Empty(contacts);
    }

    [Fact]
    public async Task ListContacts_IncludesPreviouslyCreatedContact()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var session = await ResolveAsync(channelId, externalId);

        var response = await _client.GetAsync("/contacts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contacts = await response.Content.ReadFromJsonAsync<List<ContactResponse>>();
        Assert.NotNull(contacts);
        Assert.Contains(contacts, c => c.Id == session.ContactId && c.ChannelId == channelId && c.ExternalId == externalId);
    }

    [Fact]
    public async Task GetContactSessions_ExistingContactWithSession_ReturnsSessions()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var session = await ResolveAsync(channelId, externalId);

        var response = await _client.GetAsync($"/contacts/{session.ContactId}/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionResponse>>();
        Assert.NotNull(sessions);
        Assert.Contains(sessions, s => s.Id == session.Id && s.ContextId == session.ContextId);
    }

    [Fact]
    public async Task GetContactSessions_ExistingContactWithoutSession_ReturnsEmptyList()
    {
        var channelId = await CreateChannelAsync();
        var contactId = await CreateBareContactAsync(channelId);

        var response = await _client.GetAsync($"/contacts/{contactId}/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionResponse>>();
        Assert.NotNull(sessions);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task GetContactSessions_MissingContact_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/contacts/{Guid.NewGuid()}/sessions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    // Contact sem nenhuma Session — não alcançável via
    // IContactSessionResolver (Contact e a primeira Session nascem juntos
    // na mesma chamada), então esse cenário de borda é montado inserindo
    // direto no DbContext, só para exercitar o caso "200 []" de
    // GetContactSessionsAsync.
    private async Task<Guid> CreateBareContactAsync(Guid channelId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = new Contact(channelId, UniqueExternalId());
        dbContext.Contacts.Add(contact);
        await dbContext.SaveChangesAsync();
        return contact.Id;
    }
}
