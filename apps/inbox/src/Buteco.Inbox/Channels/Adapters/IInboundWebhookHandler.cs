namespace Buteco.Inbox.Channels.Adapters;

public interface IInboundWebhookHandler
{
    Task HandleAsync(Guid channelId, HttpRequest request, CancellationToken cancellationToken);
}
