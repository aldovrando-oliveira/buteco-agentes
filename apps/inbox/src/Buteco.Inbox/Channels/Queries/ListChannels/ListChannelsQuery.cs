using Buteco.Inbox.Channels.Responses;
using Mediator;

namespace Buteco.Inbox.Channels.Queries.ListChannels;

public sealed record ListChannelsQuery : IQuery<IReadOnlyList<ChannelResponse>>;
