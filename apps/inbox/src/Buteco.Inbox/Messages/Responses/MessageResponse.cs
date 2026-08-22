using Buteco.Inbox.Messages.Entities;

namespace Buteco.Inbox.Messages.Responses;

public sealed record MessageResponse(
    Guid Id,
    MessageDirection Direction,
    string Content,
    MessageContentType ContentType,
    DateTimeOffset OccurredAt,
    string? ExternalId,
    MessageDeliveryStatus? DeliveryStatus,
    string? DeliveryFailureReason,
    MessageDispatchStatus? DispatchStatus)
{
    public static MessageResponse FromEntity(Message message) => new(
        message.Id,
        message.Direction,
        message.Content,
        message.ContentType,
        message.OccurredAt,
        message.ExternalId,
        message.DeliveryStatus,
        message.DeliveryFailureReason,
        message.DispatchStatus);
}
