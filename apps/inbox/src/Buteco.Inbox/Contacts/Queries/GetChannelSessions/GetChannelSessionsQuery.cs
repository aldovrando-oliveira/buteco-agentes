using Buteco.Inbox.Contacts.Responses;
using Mediator;

namespace Buteco.Inbox.Contacts.Queries.GetChannelSessions;

public sealed record GetChannelSessionsQuery(Guid ChannelId) : IQuery<IReadOnlyList<ChannelSessionResponse>?>;
