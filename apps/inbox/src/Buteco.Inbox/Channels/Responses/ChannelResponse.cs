using Buteco.Inbox.Channels.Entities;

namespace Buteco.Inbox.Channels.Responses;

/// <summary>
/// Nunca inclui as credenciais (nem em texto claro, nem criptografadas) —
/// ver Requirement de cadastro/consulta/listagem em specs/inbox-channel-catalog.
/// </summary>
public sealed record ChannelResponse(
    Guid Id,
    string ChannelType,
    string Name,
    Guid AgentId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string WebhookUrl)
{
    // publicUrlBaseUrl reaproveita PublicUrlOptions já existente (design.md,
    // Decision 4) — nunca persistido, computado a cada resposta. Nenhum
    // endpoint é mapeado nesse path nesta fatia (design.md, Non-Goals).
    public static ChannelResponse FromEntity(Channel channel, string publicUrlBaseUrl) => new(
        channel.Id,
        channel.ChannelType,
        channel.Name,
        channel.AgentId,
        channel.IsActive,
        channel.CreatedAt,
        channel.UpdatedAt,
        BuildWebhookUrl(publicUrlBaseUrl, channel.ChannelType, channel.Id));

    private static string BuildWebhookUrl(string publicUrlBaseUrl, string channelType, Guid channelId) =>
        $"{publicUrlBaseUrl.TrimEnd('/')}/webhooks/{channelType}/{channelId}";
}
