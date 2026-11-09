using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// A escrita do link de redefinição de senha (design.md da change comando-password-reset-link, D1).
/// É a mesma operação para as duas entradas, a rota do admin (<see cref="PasswordResetEndpoints"/>)
/// e o comando de emergência (<see cref="PasswordResetLinkCommand"/>), para elas não divergirem.
///
/// <list type="bullet">
/// <item><b>só a escrita:</b> quem pode pedir, e para quem, é regra de cada entrada;</item>
/// <item><b>a base do painel chega pronta:</b> resolvida antes, para uma base ausente nunca deixar o
/// link anterior morto sem um novo na mão de quem pediu;</item>
/// <item><b>uma instrução só</b>, condicionada ao estado ativo: sobrescreve o link pendente (o
/// anterior deixa de valer) e não toca a senha, o carimbo de segurança nem as sessões, que continuam
/// até a senha nova ser criada. Duas gerações ao mesmo tempo são serializadas pelo lock da linha, e
/// vale a última (D7).</item>
/// </list>
/// </summary>
public sealed class PasswordResetLink(AppDbContext dbContext, TimeProvider timeProvider)
{
    public sealed record Issued(string Link, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Grava o link de <paramref name="userId"/> e devolve o endereço e o vencimento; <c>null</c>
    /// quando o usuário não está ativo no momento da escrita (desativado entre a leitura e a escrita).
    /// </summary>
    public async Task<Issued?> IssueAsync(Guid userId, Uri panelBase, CancellationToken cancellationToken)
    {
        var token = AccessLinkToken.New();
        var hash = AccessLinkToken.Hash(token);
        var expiresAt = timeProvider.GetUtcNow() + AccessLinkToken.PasswordResetLifetime;
        var issued = await dbContext.Users
            .Where(user => user.Id == userId && user.Status == UserStatus.Active)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.AccessLinkHash, hash)
                    .SetProperty(user => user.AccessLinkPurpose, AccessLinkPurpose.PasswordReset)
                    .SetProperty(user => user.AccessLinkExpiresAt, expiresAt),
                cancellationToken);

        return issued == 1
            ? new Issued(AccessLinkUrls.For(AccessLinkPurpose.PasswordReset, panelBase, token), expiresAt)
            : null;
    }
}
