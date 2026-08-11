using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Channels.Adapters;

public static class ChannelAdapterRegistrationExtensions
{
    // Chamada uma única vez no startup, logo após todos os adapters
    // registrarem seus IChannelConfigValidator/IOutboundMessageSender via
    // AddKeyedSingleton (design.md, Decision 7). IsRegistered (usado no
    // caminho de POST/PUT /channels) só confere o validador — nada
    // impede um adapter de registrar só um dos dois serviços sob uma
    // chave. Essa lacuna só apareceria, sem esta checagem, na primeira
    // mensagem de resposta de um canal mal configurado, dentro do
    // endpoint receptor de push notification. Falhar aqui, no startup,
    // identificando o ChannelType incompleto, é mais barato de
    // diagnosticar.
    public static void ValidateChannelAdapterRegistrations(this IServiceCollection services)
    {
        var validatorKeys = KeysFor<IChannelConfigValidator>(services);
        var senderKeys = KeysFor<IOutboundMessageSender>(services);

        var problems = validatorKeys.Except(senderKeys)
            .Select(key => $"'{key}' tem IChannelConfigValidator registrado, mas nenhum IOutboundMessageSender")
            .Concat(senderKeys.Except(validatorKeys)
                .Select(key => $"'{key}' tem IOutboundMessageSender registrado, mas nenhum IChannelConfigValidator"))
            .ToList();

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
