using Buteco.Inbox.Contacts.Responses;
using Mediator;

namespace Buteco.Inbox.Contacts.Queries.GetContactSessions;

public sealed record GetContactSessionsQuery(Guid ContactId) : IQuery<IReadOnlyList<SessionResponse>?>;
