using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

public class ChannelAdapterRegistrationExtensionsTests
{
    [Fact]
    public void ValidateChannelAdapterRegistrations_ValidatorWithoutSender_Throws()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("orphan-validator");

        var exception = Assert.Throws<InvalidOperationException>(() => services.ValidateChannelAdapterRegistrations());

        Assert.Contains("orphan-validator", exception.Message);
        Assert.Contains("IChannelConfigValidator", exception.Message);
    }

    [Fact]
    public void ValidateChannelAdapterRegistrations_SenderWithoutValidator_Throws()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("orphan-sender");

        var exception = Assert.Throws<InvalidOperationException>(() => services.ValidateChannelAdapterRegistrations());

        Assert.Contains("orphan-sender", exception.Message);
        Assert.Contains("IOutboundMessageSender", exception.Message);
    }

    [Fact]
    public void ValidateChannelAdapterRegistrations_CompletePairs_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("test-channel");
        services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("test-channel");

        var exception = Record.Exception(() => services.ValidateChannelAdapterRegistrations());

        Assert.Null(exception);
    }
}
