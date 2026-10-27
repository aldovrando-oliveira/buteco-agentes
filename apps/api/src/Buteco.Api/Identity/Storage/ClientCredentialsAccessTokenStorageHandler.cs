using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Protection;

namespace Buteco.Api.Identity.Storage;

/// <summary>
/// Desliga a linha em <c>openiddict_tokens</c> só para o access token do grant
/// <c>client_credentials</c> (design.md da change limpeza-tokens-openiddict, D1). Todo
/// outro tipo de token, e o access token de qualquer outro grant, continua gravado.
///
/// <para>
/// Pela fonte do OpenIddict 7.7.2 (tag <c>7.7.2</c>, <c>src/OpenIddict.Server/</c>):
/// <list type="bullet">
/// <item>cada token emitido passa por um <see cref="GenerateTokenContext"/>, cujo
/// <c>CreateTokenEntry</c> sai de <c>!DisableTokenStorage</c> (F1,
/// <c>OpenIddictServerHandlers.cs</c> l. 4865) e tem <c>set</c> público (F2,
/// <c>OpenIddictServerEvents.Protection.cs</c> l. 47);</item>
/// <item>o handler que grava, <see cref="CreateTokenEntry"/>, tem o filtro
/// <c>RequireTokenEntryCreated</c>, e roda em <c>AttachSecurityCredentials.Order + 1_000</c>
/// (F3, <c>OpenIddictServerHandlers.Protection.cs</c> l. 1516–1520;
/// <c>OpenIddictServerHandlerFilters.cs</c> l. 717–726);</item>
/// <item>o dispatcher avalia o filtro na hora de chamar o handler, e não antes (F4,
/// <c>OpenIddictServerDispatcher.cs</c> l. 40–45 e 85–122). Pôr <c>false</c> aqui, antes
/// dele, faz a linha não nascer;</item>
/// <item>sem a linha, o token não carrega <c>oi_tkn_id</c> (F5, l. 1528–1577), e nenhum
/// validador consulta a linha: <c>apps/inbox</c> e <c>apps/connectors</c> validam pelo
/// JWKS (#208).</item>
/// </list>
/// A ordem sai do descritor do próprio OpenIddict, e não de um número copiado: um upgrade
/// que mude a posição do <see cref="CreateTokenEntry"/> leva este handler junto.
/// </para>
/// </summary>
public sealed class ClientCredentialsAccessTokenStorageHandler : IOpenIddictServerHandler<GenerateTokenContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<GenerateTokenContext>()
            .UseSingletonHandler<ClientCredentialsAccessTokenStorageHandler>()
            .SetOrder(CreateTokenEntry.Descriptor.Order - 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(GenerateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.TokenType == TokenTypeIdentifiers.AccessToken &&
            context.Request is not null &&
            context.Request.IsClientCredentialsGrantType())
        {
            context.CreateTokenEntry = false;
        }

        return ValueTask.CompletedTask;
    }
}
