using System.Text.Json.Serialization;

namespace Buteco.Inbox.Messages.Entities;

// Só sucesso/falha do nosso envio ao provedor — não recibo de entrega/leitura
// (design.md, Decisão 4).
[JsonConverter(typeof(JsonStringEnumConverter<MessageDeliveryStatus>))]
public enum MessageDeliveryStatus
{
    Sent,
    Failed,
}
