using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Protection;

namespace Buteco.Api.Identity.Storage;

/// <summary>
/// Desliga a linha em <c>openiddict_tokens</c> para todo access token, de qualquer grant,
/// e para todo id token (design.md da change usuarios-locais-login-emissor, D3.8, que
/// estende o D1 da limpeza-tokens-openiddict). Os três apps validam pelo JWKS, e ninguém
/// lê essas linhas. Continuam gravados o código de autorização (resgate único) e o refresh
/// token (revogação, rotação e reuso). Era o <c>ClientCredentialsAccessTokenStorageHandler</c>.
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
/// validador consulta a linha: os três apps validam pelo JWKS (#208, #180 D3.3). O id token
/// sem <c>oi_tkn_id</c> também não consulta linha quando volta como <c>id_token_hint</c>
/// (2.4 (b) da #180).</item>
/// </list>
/// A ordem sai do descritor do próprio OpenIddict, e não de um número copiado: um upgrade
/// que mude a posição do <see cref="CreateTokenEntry"/> leva este handler junto.
/// </para>
/// </summary>
public sealed class TokenEntryStorageHandler : IOpenIddictServerHandler<GenerateTokenContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<GenerateTokenContext>()
            .UseSingletonHandler<TokenEntryStorageHandler>()
            .SetOrder(CreateTokenEntry.Descriptor.Order - 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(GenerateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.TokenType is TokenTypeIdentifiers.AccessToken or TokenTypeIdentifiers.IdentityToken)
        {
            context.CreateTokenEntry = false;
        }

        return ValueTask.CompletedTask;
    }
}
