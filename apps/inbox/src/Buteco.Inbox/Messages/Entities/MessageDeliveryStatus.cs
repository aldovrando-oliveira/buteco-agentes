namespace Buteco.Inbox.Messages.Entities;

// Só sucesso/falha do nosso envio ao provedor — não recibo de entrega/leitura
// (design.md, Decisão 4).
public enum MessageDeliveryStatus
{
    Sent,
    Failed,
}
