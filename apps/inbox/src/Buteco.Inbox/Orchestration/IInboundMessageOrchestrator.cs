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
    //
    // receivedAt TEM de ser o relógio do inbox no recebimento do webhook
    // (DateTimeOffset.UtcNow), NUNCA o horário informado pelo provedor. Ele vira
    // PendingDispatch.LastMessageAt, e a reconciliação encerra como perda a linha
    // em Dispatching sem TaskId pela idade desse valor (change
    // pending-dispatch-orfa, #47, design.md D7). Um instante do provedor — uma
    // mensagem entregue com atraso depois de o webhook ficar fora — faria a linha
    // nascer "velha" e ser encerrada com o SendMessage em andamento. Um adapter
    // que precise passar outro instante é o gatilho da coluna DispatchingSince
    // registrada no D7.
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
