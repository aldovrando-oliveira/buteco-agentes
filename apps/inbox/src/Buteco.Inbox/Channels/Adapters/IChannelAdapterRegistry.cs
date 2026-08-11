namespace Buteco.Inbox.Channels.Adapters;

public interface IChannelAdapterRegistry
{
    bool IsRegistered(string channelType);

    IChannelConfigValidator GetConfigValidator(string channelType);

    IOutboundMessageSender GetOutboundMessageSender(string channelType);
}
