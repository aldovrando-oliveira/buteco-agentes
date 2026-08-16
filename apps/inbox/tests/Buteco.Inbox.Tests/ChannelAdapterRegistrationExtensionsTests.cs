using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

public class ChannelAdapterRegistrationExtensionsTests
{
    [Fact]
    public void ValidateChannelAdapterRegistrations_OnlyValidator_ThrowsNamingMissingSenderAndWebhookHandler()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("orphan-validator");

        var exception = Assert.Throws<InvalidOperationException>(() => services.ValidateChannelAdapterRegistrations());

        Assert.Contains("orphan-validator", exception.Message);
        Assert.Contains("IOutboundMessageSender", exception.Message);
        Assert.Contains("IInboundWebhookHandler", exception.Message);
    }

    [Fact]
    public void ValidateChannelAdapterRegistrations_OnlySender_ThrowsNamingMissingValidatorAndWebhookHandler()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("orphan-sender");

        var exception = Assert.Throws<InvalidOperationException>(() => services.ValidateChannelAdapterRegistrations());

        Assert.Contains("orphan-sender", exception.Message);
        Assert.Contains("IChannelConfigValidator", exception.Message);
        Assert.Contains("IInboundWebhookHandler", exception.Message);
    }

    [Fact]
    public void ValidateChannelAdapterRegistrations_OnlyWebhookHandler_ThrowsNamingMissingValidatorAndSender()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IInboundWebhookHandler, TestInboundWebhookHandler>("orphan-webhook-handler");

        var exception = Assert.Throws<InvalidOperationException>(() => services.ValidateChannelAdapterRegistrations());

        Assert.Contains("orphan-webhook-handler", exception.Message);
        Assert.Contains("IChannelConfigValidator", exception.Message);
        Assert.Contains("IOutboundMessageSender", exception.Message);
    }

    [Fact]
    public void ValidateChannelAdapterRegistrations_ValidatorAndSenderWithoutWebhookHandler_ThrowsNamingMissingWebhookHandler()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("missing-webhook-handler");
        services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("missing-webhook-handler");

        var exception = Assert.Throws<InvalidOperationException>(() => services.ValidateChannelAdapterRegistrations());

        Assert.Contains("missing-webhook-handler", exception.Message);
        Assert.Contains("IInboundWebhookHandler", exception.Message);
    }

    [Fact]
    public void ValidateChannelAdapterRegistrations_CompleteTriples_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("test-channel");
        services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("test-channel");
        services.AddKeyedSingleton<IInboundWebhookHandler, TestInboundWebhookHandler>("test-channel");

        var exception = Record.Exception(() => services.ValidateChannelAdapterRegistrations());

        Assert.Null(exception);
    }

    // Quarto contrato, opcional (inbox-adapter-telegram, design.md,
    // Decision 3) — os três testes abaixo provam os dois lados dessa
    // opcionalidade.
    [Fact]
    public void ValidateChannelAdapterRegistrations_CompleteTriplesWithoutProvisioner_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("test-channel");
        services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("test-channel");
        services.AddKeyedSingleton<IInboundWebhookHandler, TestInboundWebhookHandler>("test-channel");

        var exception = Record.Exception(() => services.ValidateChannelAdapterRegistrations());

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateChannelAdapterRegistrations_CompleteQuadrupleWithProvisioner_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("test-channel");
        services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("test-channel");
        services.AddKeyedSingleton<IInboundWebhookHandler, TestInboundWebhookHandler>("test-channel");
        services.AddKeyedSingleton<IChannelWebhookProvisioner, TestWebhookProvisioner>("test-channel");

        var exception = Record.Exception(() => services.ValidateChannelAdapterRegistrations());

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateChannelAdapterRegistrations_ProvisionerWithoutRequiredContracts_ThrowsNamingProvisioner()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChannelWebhookProvisioner, TestWebhookProvisioner>("orphan-provisioner");

        var exception = Assert.Throws<InvalidOperationException>(() => services.ValidateChannelAdapterRegistrations());

        Assert.Contains("orphan-provisioner", exception.Message);
        Assert.Contains(nameof(IChannelWebhookProvisioner), exception.Message);
    }

    private sealed class TestWebhookProvisioner : IChannelWebhookProvisioner
    {
        public Task<ChannelWebhookProvisioningResult> ProvisionAsync(Guid channelId, string credential, string webhookUrl, CancellationToken cancellationToken) =>
            Task.FromResult(ChannelWebhookProvisioningResult.Succeeded(credential));
    }
}
