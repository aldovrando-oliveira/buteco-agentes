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
    // publicUrlBaseUrl reaproveita PublicUrlOptions já existente
    // (inbox-adapter-contrato-catalogo, design.md, Decision 4) — nunca
    // persistido, computado a cada resposta. Formato /webhooks/{channelId}
    // (sem channelType na rota, inbox-adapter-waha, design.md, ajuste de
    // convenção) — o ChannelType já está persistido no Channel resolvido
    // por channelId e é imutável após a criação, carregá-lo também na rota
    // seria redundante.
    public static ChannelResponse FromEntity(Channel channel, string publicUrlBaseUrl) => new(
        channel.Id,
        channel.ChannelType,
        channel.Name,
        channel.AgentId,
        channel.IsActive,
        channel.CreatedAt,
        channel.UpdatedAt,
        BuildWebhookUrl(publicUrlBaseUrl, channel.Id));

    // internal, não private — reaproveitado por CreateChannelCommandHandler/
    // UpdateChannelCommandHandler para computar a mesma URL antes de invocar
    // um IChannelWebhookProvisioner, sem duplicar o formato "/webhooks/{id}"
    // (inbox-adapter-telegram, design.md, Decision 4).
    internal static string BuildWebhookUrl(string publicUrlBaseUrl, Guid channelId) =>
        $"{publicUrlBaseUrl.TrimEnd('/')}/webhooks/{channelId}";
}
