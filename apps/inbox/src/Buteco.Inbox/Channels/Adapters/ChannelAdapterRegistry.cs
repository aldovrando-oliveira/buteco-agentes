using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Channels.Adapters;

public sealed class ChannelAdapterRegistry(IServiceProvider serviceProvider) : IChannelAdapterRegistry
{
    public bool IsRegistered(string channelType) =>
        serviceProvider.GetKeyedService<IChannelConfigValidator>(channelType) is not null;

    public IChannelConfigValidator GetConfigValidator(string channelType) =>
        serviceProvider.GetRequiredKeyedService<IChannelConfigValidator>(channelType);

    public IOutboundMessageSender GetOutboundMessageSender(string channelType) =>
        serviceProvider.GetRequiredKeyedService<IOutboundMessageSender>(channelType);
}
