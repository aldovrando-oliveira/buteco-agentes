namespace Buteco.Inbox.Orchestration;

public interface IInboundMessageOrchestrator
{
    Task ReceiveMessageAsync(
        Guid channelId,
        string externalId,
        string text,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken);
}
