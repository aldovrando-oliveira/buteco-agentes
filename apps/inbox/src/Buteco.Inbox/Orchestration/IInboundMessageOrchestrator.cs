using Buteco.Inbox.Messages.Entities;

namespace Buteco.Inbox.Orchestration;

public interface IInboundMessageOrchestrator
{
    // contactMetadata obrigatório, propagado até IContactSessionResolver
    // (inbox-adapter-waha, design.md, Decision 8) — mesmo raciocínio de
    // parâmetro explícito, não opcional. externalMessageId (identificador
    // de mensagem do provedor) e displayName (nullable) foram adicionados
    // por inbox-mensagens-persistidas, design.md, Decisões 5 e 9 — o
    // primeiro usado para deduplicar webhook reentregue, o segundo
    // atualizado a cada chamada em Contact.DisplayName.
    Task ReceiveMessageAsync(
        Guid channelId,
        string externalId,
        string text,
        MessageContentType contentType,
        string externalMessageId,
        string? displayName,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<string, string> contactMetadata,
        CancellationToken cancellationToken);
}
