using Buteco.Inbox.Contacts.Entities;

namespace Buteco.Inbox.Contacts;

public interface IContactSessionResolver
{
    Task<Session> FindOrCreateSessionAsync(Guid channelId, string externalId, CancellationToken cancellationToken);
}
