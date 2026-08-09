using Buteco.Inbox.Channels.Responses;
using Mediator;

namespace Buteco.Inbox.Channels.Queries.GetChannelById;

public sealed record GetChannelByIdQuery(Guid Id) : IQuery<ChannelResponse?>;
