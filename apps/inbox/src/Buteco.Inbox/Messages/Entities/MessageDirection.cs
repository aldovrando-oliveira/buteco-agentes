using System.Text.Json.Serialization;

namespace Buteco.Inbox.Messages.Entities;

[JsonConverter(typeof(JsonStringEnumConverter<MessageDirection>))]
public enum MessageDirection
{
    Inbound,
    Outbound,
}
