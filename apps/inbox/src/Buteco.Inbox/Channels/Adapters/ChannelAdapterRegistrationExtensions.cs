using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Channels.Adapters;

public static class ChannelAdapterRegistrationExtensions
{
    // Chamada uma única vez no startup, logo após todos os adapters
    // registrarem seus IChannelConfigValidator/IOutboundMessageSender/
    // IInboundWebhookHandler via AddKeyedSingleton (design.md, Decision 2
    // de inbox-adapter-waha, estendendo a Decision 7 de
    // inbox-adapter-contrato-catalogo de dois para três contratos).
    // IsRegistered (usado no caminho de POST/PUT /channels) só confere o
    // validador — nada impede um adapter de registrar só parte dos três
    // serviços sob uma chave. Essa lacuna só apareceria, sem esta
    // checagem, na primeira mensagem/resposta/webhook de um canal mal
    // configurado. Falhar aqui, no startup, identificando o ChannelType
    // incompleto, é mais barato de diagnosticar.
    public static void ValidateChannelAdapterRegistrations(this IServiceCollection services)
    {
        var validatorKeys = KeysFor<IChannelConfigValidator>(services);
        var senderKeys = KeysFor<IOutboundMessageSender>(services);
        var webhookHandlerKeys = KeysFor<IInboundWebhookHandler>(services);
        var provisionerKeys = KeysFor<IChannelWebhookProvisioner>(services);

        var contracts = new (string Name, HashSet<object> Keys)[]
        {
            (nameof(IChannelConfigValidator), validatorKeys),
            (nameof(IOutboundMessageSender), senderKeys),
            (nameof(IInboundWebhookHandler), webhookHandlerKeys),
        };

        var requiredKeys = validatorKeys.Union(senderKeys).Union(webhookHandlerKeys);

        var problems = requiredKeys
            .SelectMany(key => contracts
                .Where(contract => !contract.Keys.Contains(key))
                .Select(contract => $"'{key}' não tem {contract.Name} registrado"))
            .ToList();

        // IChannelWebhookProvisioner é o quarto contrato, opcional
        // (inbox-adapter-telegram, design.md, Decision 3) — não entra em
        // "contracts" acima, então sua ausência para um ChannelType com os
        // três obrigatórios não é um problema. Só é erro registrá-lo sem os
        // três obrigatórios por trás.
        problems.AddRange(provisionerKeys
            .Where(key => !requiredKeys.Contains(key))
            .Select(key => $"'{key}' tem {nameof(IChannelWebhookProvisioner)} registrado sem os três contratos obrigatórios"));

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Registro de adapters de canal incompleto: {string.Join("; ", problems)}.");
        }
    }

    private static HashSet<object> KeysFor<TService>(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(TService) && descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceKey!)
            .ToHashSet();
}
