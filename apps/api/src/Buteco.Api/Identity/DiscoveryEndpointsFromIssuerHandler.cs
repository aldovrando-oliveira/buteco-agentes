using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Discovery;

namespace Buteco.Api.Identity;

/// <summary>
/// Faz o discovery montar os endereços dos endpoints a partir do issuer, e não do host da
/// requisição (design.md da change emissor-oidc-api, D8, "Correção na implementação").
/// <para>
/// No OpenIddict 7.7.2 só o campo <c>issuer</c> sai da configuração: o
/// <see cref="AttachEndpoints"/> monta cada endpoint com <c>CreateAbsoluteUri(context.BaseUri, …)</c>,
/// e o <c>BaseUri</c> vem do esquema e do <c>Host</c> da requisição. Atrás do nginx, que
/// repassa HTTP puro, isso anunciaria <c>http://</c> sob um issuer <c>https://</c>.
/// </para>
/// <para>
/// Este handler roda entre o <see cref="AttachIssuer"/> e o <see cref="AttachEndpoints"/> e
/// troca o <c>BaseUri</c> da transação de discovery pelo issuer já resolvido. Assim o
/// próprio <see cref="AttachEndpoints"/> monta todo endpoint configurado, inclusive os que
/// uma change futura habilitar, sem lista mantida aqui; endpoint sem URI configurada
/// continua fora do documento. A troca vale só para esta transação: o casamento da rota já
/// aconteceu e nada depois dele no discovery usa o <c>BaseUri</c>.
/// </para>
/// </summary>
public sealed class DiscoveryEndpointsFromIssuerHandler : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<HandleConfigurationRequestContext>()
            .UseSingletonHandler<DiscoveryEndpointsFromIssuerHandler>()
            .SetOrder(AttachIssuer.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Issuer is { IsAbsoluteUri: true } issuer)
        {
            context.BaseUri = issuer;
        }

        return ValueTask.CompletedTask;
    }
}
