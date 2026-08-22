using Buteco.Inbox.Messages.Responses;
using Mediator;

namespace Buteco.Inbox.Messages.Queries.GetSessionMessages;

public sealed record GetSessionMessagesQuery(Guid SessionId) : IQuery<IReadOnlyList<MessageResponse>?>;
