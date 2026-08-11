namespace Buteco.Inbox.Channels.Adapters;

public interface IOutboundMessageSender
{
    Task SendAsync(OutboundMessage message, CancellationToken cancellationToken);
}
