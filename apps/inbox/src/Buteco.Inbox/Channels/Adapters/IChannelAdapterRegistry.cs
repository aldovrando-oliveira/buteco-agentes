namespace Buteco.Inbox.Channels.Adapters;

public interface IChannelAdapterRegistry
{
    bool IsRegistered(string channelType);

    IChannelConfigValidator GetConfigValidator(string channelType);

    IOutboundMessageSender GetOutboundMessageSender(string channelType);

    // Retorna null (não GetRequiredKeyedService) quando não registrado — o
    // endpoint genérico de webhook (design.md, Decision 1) precisa
    // distinguir "tipo sem handler" de uma falha inesperada, para responder
    // HTTP 400 em vez de deixar uma exceção não tratada virar 500.
    IInboundWebhookHandler? GetInboundWebhookHandler(string channelType);

    // Quarto contrato, opcional — null quando o adapter do ChannelType não
    // implementa provisionamento automático (ex. "waha"), diferente dos
    // três contratos acima, que são obrigatórios em conjunto
    // (inbox-adapter-telegram, design.md, Decision 1).
    IChannelWebhookProvisioner? GetWebhookProvisioner(string channelType);
}
