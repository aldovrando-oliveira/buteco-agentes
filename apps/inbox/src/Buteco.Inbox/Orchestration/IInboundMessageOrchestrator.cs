namespace Buteco.Inbox.Orchestration;

public interface IInboundMessageOrchestrator
{
    // contactMetadata obrigatório, propagado até IContactSessionResolver
    // (inbox-adapter-waha, design.md, Decision 8) — mesmo raciocínio de
    // parâmetro explícito, não opcional.
    Task ReceiveMessageAsync(
        Guid channelId,
        string externalId,
        string text,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<string, string> contactMetadata,
        CancellationToken cancellationToken);
}
