namespace Buteco.Inbox.Messages.Entities;

// Histórico durável de mensagens por Session — tabela relacional própria,
// separada do buffer de debounce (PendingDispatch.Messages, owned/JSON,
// efêmero) para não tocar a área do bug de concorrência corrigido em
// inbox-fix-concorrencia-orquestrador (design.md, Decisão 1).
public class Message
{
    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public MessageDirection Direction { get; private set; }

    public string Content { get; private set; } = null!;

    public MessageContentType ContentType { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    // Só Inbound — identificador de mensagem do provedor, usado para
    // deduplicar webhook reentregue (design.md, Decisão 5). Null em Outbound.
    public string? ExternalId { get; private set; }

    // Só Outbound — resultado do IOutboundMessageSender.SendAsync
    // (design.md, Decisão 4). Null em Inbound.
    public MessageDeliveryStatus? DeliveryStatus { get; private set; }

    public string? DeliveryFailureReason { get; private set; }

    // Só Inbound — correlação opcional com o ciclo de dispatch que consumiu
    // esta mensagem (design.md, Decisão 6). Sem FK real — a linha de
    // PendingDispatch referenciada é removida ao atingir estado terminal.
    public Guid? PendingDispatchId { get; private set; }

    public MessageDispatchStatus? DispatchStatus { get; private set; }

    private Message()
    {
    }

    public static Message CreateInbound(
        Guid sessionId,
        string content,
        MessageContentType contentType,
        DateTimeOffset occurredAt,
        string externalId,
        Guid pendingDispatchId) =>
        new()
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Direction = MessageDirection.Inbound,
            Content = content,
            ContentType = contentType,
            OccurredAt = occurredAt,
            ExternalId = externalId,
            PendingDispatchId = pendingDispatchId,
            DispatchStatus = MessageDispatchStatus.Pending,
        };

    // ContentType sempre Text — nenhum caminho de saída produz outro tipo
    // nesta fatia (design.md, Decisão 3).
    public static Message CreateOutbound(
        Guid sessionId,
        string content,
        DateTimeOffset occurredAt,
        MessageDeliveryStatus deliveryStatus,
        string? deliveryFailureReason) =>
        new()
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Direction = MessageDirection.Outbound,
            Content = content,
            ContentType = MessageContentType.Text,
            OccurredAt = occurredAt,
            DeliveryStatus = deliveryStatus,
            DeliveryFailureReason = deliveryFailureReason,
        };

    public void UpdateDispatchStatus(MessageDispatchStatus status)
    {
        DispatchStatus = status;
    }
}
