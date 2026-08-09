using Buteco.Inbox.Contacts.Entities;

namespace Buteco.Inbox.Contacts.Responses;

public sealed record SessionResponse(
    Guid Id,
    Guid ContactId,
    string ContextId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? ClosedAt)
{
    public static SessionResponse FromEntity(Session session) => new(
        session.Id,
        session.ContactId,
        session.ContextId,
        session.StartedAt,
        session.LastActivityAt,
        session.ClosedAt);
}
