using Buteco.Inbox.Channels.Responses;
using Mediator;

namespace Buteco.Inbox.Channels.Commands.DeactivateChannel;

public sealed record DeactivateChannelCommand(Guid Id) : ICommand<ChannelResponse?>;
