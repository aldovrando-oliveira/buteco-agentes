using Buteco.Api.Infrastructure;
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
/// que impede um refresh de antes da desativação de renovar depois de uma reativação.</item>
/// </list>
/// No <c>refresh_token</c> aceito, atualiza o último acesso (D3.11); no login, quem atualiza
/// é o <c>POST /auth/session</c>.
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

        if (user is null || !user.IsActive || user.SecurityStamp != stamp)
        {
            context.Reject(
                error: Errors.InvalidGrant,
                description: "O usuário da sessão não está mais autorizado a obter tokens.");
            return;
        }

        if (context.Request.IsRefreshTokenGrantType())
        {
            user.RecordAccess(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
    }
}
