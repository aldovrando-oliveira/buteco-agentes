using Buteco.Inbox.Contacts.Entities;

namespace Buteco.Inbox.Contacts.Responses;

public sealed record ContactResponse(
    Guid Id,
    Guid ChannelId,
    string ExternalId,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt)
{
    public static ContactResponse FromEntity(Contact contact) => new(
        contact.Id,
        contact.ChannelId,
        contact.ExternalId,
        contact.Metadata,
        contact.CreatedAt);
}
