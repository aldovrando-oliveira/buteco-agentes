using Buteco.Inbox.Channels.Responses;
using Mediator;

namespace Buteco.Inbox.Channels.Commands.ActivateChannel;

public sealed record ActivateChannelCommand(Guid Id) : ICommand<ChannelResponse?>;
