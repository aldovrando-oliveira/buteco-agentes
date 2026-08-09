using Buteco.Inbox.Channels.Entities;

namespace Buteco.Inbox.Channels.Responses;

/// <summary>
/// Nunca inclui as credenciais (nem em texto claro, nem criptografadas) —
/// ver Requirement de cadastro/consulta/listagem em specs/inbox-channel-catalog.
/// </summary>
public sealed record ChannelResponse(
    Guid Id,
    ChannelType ChannelType,
    string Name,
    Guid AgentId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ChannelResponse FromEntity(Channel channel) => new(
        channel.Id,
        channel.ChannelType,
        channel.Name,
        channel.AgentId,
        channel.IsActive,
        channel.CreatedAt,
        channel.UpdatedAt);
}
