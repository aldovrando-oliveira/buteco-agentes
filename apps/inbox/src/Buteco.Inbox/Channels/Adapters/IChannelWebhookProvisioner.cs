namespace Buteco.Inbox.Channels.Adapters;

// Quarto contrato de plugin, opcional — ao contrário de IChannelConfigValidator/
// IOutboundMessageSender/IInboundWebhookHandler, nem todo ChannelType precisa
// implementá-lo (design.md, Decision 1). Nome "Webhook", não "Activation":
// o catálogo já tem ActivateChannelCommand/DeactivateChannelCommand para o
// campo de estado IsActive, sem relação com este contrato.
public interface IChannelWebhookProvisioner
{
    Task<ChannelWebhookProvisioningResult> ProvisionAsync(
        Guid channelId,
        string credential,
        string webhookUrl,
        CancellationToken cancellationToken);
}
