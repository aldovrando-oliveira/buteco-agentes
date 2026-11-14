using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Client.OpenIddictClientEvents;
using static OpenIddict.Client.OpenIddictClientHandlers;
using static OpenIddict.Client.OpenIddictClientHandlerFilters;

namespace Buteco.Api.Identity.Federated;

/// <summary>
/// O retorno com a registração do state token fora da configuração vira <b>rejeição</b>, e não exceção
/// (design.md da change aceite-convite-google, D3 e D9; issue #276). Sem ele, o
/// <c>ResolveClientRegistrationFromStateToken</c> do OpenIddict 7.7.2 chama o
/// <c>GetClientRegistrationByIdAsync</c>, que lança quando a registração não está nas opções
/// (<c>OpenIddictClientService.cs</c> l. 153–155), dentro do middleware de autenticação: o retorno respondia
/// <c>500</c> antes de chegar à rota. Acontece quando a chave do provedor muda entre o início e o retorno (na
/// M3, um provedor por issuer: tirar o único já cai no "nenhum provedor configurado" da rota).
///
/// Roda logo antes do <c>ResolveClientRegistrationFromStateToken</c>, com os mesmos filtros (o state token já
/// conferido, e já resgatado pelo <c>RedeemStateTokenEntry</c>, que vem antes). Com o
/// <c>EnableErrorPassthrough</c>, a rejeição chega ao <c>AuthenticateAsync</c> da rota como falha, e a rota
/// responde a recusa <c>falha</c> da intenção.
/// </summary>
public sealed class FederatedMissingRegistrationHandler(IOptionsMonitor<OpenIddictClientOptions> options) : IOpenIddictClientHandler<ProcessAuthenticationContext>
{
    public const string ErrorDescription = "A registração do provedor deste retorno não está mais na configuração.";

    public static OpenIddictClientHandlerDescriptor Descriptor { get; } =
        OpenIddictClientHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
            .AddFilter<RequireStateTokenPrincipal>()
            .AddFilter<RequireStateTokenValidated>()
            .UseSingletonHandler<FederatedMissingRegistrationHandler>()
            .SetOrder(ResolveClientRegistrationFromStateToken.Descriptor.Order - 500)
            .SetType(OpenIddictClientHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(ProcessAuthenticationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var registrationId = context.StateTokenPrincipal?.GetClaim(Claims.Private.RegistrationId);
        if (string.IsNullOrEmpty(registrationId) ||
            options.CurrentValue.Registrations.Exists(registration => string.Equals(registration.RegistrationId, registrationId, StringComparison.Ordinal)))
        {
            return ValueTask.CompletedTask;
        }

        context.Reject(error: Errors.InvalidRequest, description: ErrorDescription);
        return ValueTask.CompletedTask;
    }
}
