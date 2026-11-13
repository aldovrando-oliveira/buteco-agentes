using Microsoft.AspNetCore;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Client.OpenIddictClientEvents;

namespace Buteco.Api.Identity.Federated;

/// <summary>
/// A URI da requisição que o cliente OIDC compara com a <c>redirect_uri</c> do pedido (ID2138, a defesa
/// contra mix-up do próprio OpenIddict) sai da <b>base do painel</b>, e não do esquema e do host da
/// requisição (design.md da change provedores-federados-google, D1; registro do apply). O TLS termina fora
/// do stack e o <c>apps/api</c> vê HTTP do nginx interno, sem <c>ForwardedHeaders</c>: o esquema da
/// requisição nunca seria o <c>https</c> registrado no provedor. É o mesmo princípio do servidor, cujos
/// endereços saem do issuer (<c>DiscoveryEndpointsFromIssuerHandler</c>). O caminho e a query continuam os
/// da requisição, e a comparação deles continua valendo.
/// </summary>
public sealed class FederatedRequestUriHandler(FederatedRedirect redirect) : IOpenIddictClientHandler<ProcessRequestContext>
{
    public static OpenIddictClientHandlerDescriptor Descriptor { get; } =
        OpenIddictClientHandlerDescriptor.CreateBuilder<ProcessRequestContext>()
            .UseSingletonHandler<FederatedRequestUriHandler>()
            .SetOrder(OpenIddictClientAspNetCoreHandlers.ResolveRequestUri.Descriptor.Order + 1)
            .SetType(OpenIddictClientHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(ProcessRequestContext context)
    {
        var request = context.Transaction.GetHttpRequest();
        if (request is null)
        {
            return ValueTask.CompletedTask;
        }

        var path = $"{request.PathBase}{request.Path}{request.QueryString}".TrimStart('/');
        context.BaseUri = redirect.PanelBase;
        context.RequestUri = new Uri(redirect.PanelBase, path);
        return ValueTask.CompletedTask;
    }
}

/// <summary>A base do painel e a URI de retorno registrada no provedor (<c>&lt;base&gt;/auth/federated/callback</c>).</summary>
public sealed record FederatedRedirect(Uri PanelBase, Uri RedirectUri);
