using System.Security.Claims;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Buteco.Api.Identity.Flow;

/// <summary>
/// Todo token novo de usuário confere estado e stamp (design.md da change
/// usuarios-locais-login-emissor, D4, decisão do dono de 31/10/2026). Nos grants
/// <c>authorization_code</c> e <c>refresh_token</c>, depois de o OpenIddict restaurar o
/// principal (<c>AttachPrincipal</c>, ordem <c>int.MinValue + 100_000</c>, 2.4 (e)), relê o
/// usuário e recusa com <c>invalid_grant</c>:
/// <list type="bullet">
/// <item>o que não existe mais;</item>
/// <item>o desativado;</item>
/// <item>o de <c>security_stamp</c> gravado diferente do claim do código ou do refresh. É o
/// que impede um refresh de antes da desativação de renovar depois de uma reativação;</item>
/// <item>o que não tem a verificação em 2 etapas ligada (design.md da change
/// verificacao-2-etapas-obrigatoria, D4). É o que encerra, na renovação seguinte, as sessões
/// abertas antes da obrigatoriedade. A recusa é o <c>400</c> com <c>error: invalid_grant</c> do
/// endpoint de token, que o painel já trata como sessão encerrada
/// (<c>apps/frontend/src/auth/renewal.ts</c>).</item>
/// </list>
/// No <c>refresh_token</c> aceito, atualiza o último acesso (D3.11); no login, quem atualiza
/// é o <c>POST /auth/session</c>.
///
/// <para>
/// <b>No <c>refresh_token</c> aceito, também troca no principal o nome e o e-mail pelos
/// gravados</b> (design.md da change rotas-conta-usuarios, D5). O principal da renovação é o
/// do refresh, que é o do login (<c>OpenIddictServerHandlers.Exchange.cs</c> l. 2132–2135), e
/// sem a troca um nome editado nunca chegaria ao id token, que é de onde o painel o lê. O
/// <c>SetClaim</c> cria um claim novo, sem o destino do antigo, e um claim sem destino sai do
/// id token (<c>OpenIddictServerHandlers.cs</c> l. 4648–4653, conferido na 2.3 da change): por
/// isso o destino é gravado de novo, só o id token, como no <see cref="UserPrincipalFactory"/>.
/// </para>
///
/// <para>
/// <b>Token de usuário é o que traz o claim do stamp</b>, que só o authorize põe
/// (<see cref="UserPrincipalFactory"/>). O código e o refresh são cifrados e assinados pelo
/// emissor, então ninguém fabrica um sem ele. Um refresh de cliente (<c>client_credentials</c>
/// com <c>offline_access</c>, que só os testes da #209 usam) não traz o claim e não passa
/// por aqui.
/// </para>
/// </summary>
public sealed class UserGrantTokenHandler(AppDbContext dbContext, TimeProvider timeProvider)
    : IOpenIddictServerHandler<HandleTokenRequestContext>
{
    public async ValueTask HandleAsync(HandleTokenRequestContext context)
    {
        if (!context.Request.IsAuthorizationCodeGrantType() && !context.Request.IsRefreshTokenGrantType())
        {
            return;
        }

        var principal = context.Principal;
        var stamp = principal?.GetClaim(IssuerSession.SecurityStampClaim);
        if (stamp is null)
        {
            return;
        }

        var user = Guid.TryParse(principal!.GetClaim(Claims.Subject), out var userId)
            ? await dbContext.Users.FindAsync([userId], context.CancellationToken)
            : null;

        if (user is null || !user.IsActive || user.SecurityStamp != stamp || !user.IsTwoFactorEnabled)
        {
            context.Reject(
                error: Errors.InvalidGrant,
                description: "O usuário da sessão não está mais autorizado a obter tokens.");
            return;
        }

        if (context.Request.IsRefreshTokenGrantType())
        {
            RefreshProfileClaims(principal!, user);
            user.RecordAccess(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
    }

    private static void RefreshProfileClaims(ClaimsPrincipal principal, User user)
    {
        principal.SetClaim(Claims.Name, user.Name).SetClaim(Claims.Email, user.Email);
        principal.FindFirst(Claims.Name)!.SetDestinations(Destinations.IdentityToken);
        principal.FindFirst(Claims.Email)!.SetDestinations(Destinations.IdentityToken);
    }
}
