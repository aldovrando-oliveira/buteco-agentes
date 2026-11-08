using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// A redefinição da verificação em 2 etapas de um usuário (design.md da change
/// verificacao-2-etapas-totp, D9; e da change verificacao-2-etapas-obrigatoria, D6). É a mesma
/// operação para as duas entradas, a rota do admin (<see cref="UserTwoFactorResetEndpoint"/>) e o
/// comando de emergência (<see cref="TwoFactorEmergencyCommand"/>), para elas não divergirem.
///
/// Numa transação: apaga o segredo, o cadastro pendente, a ativação, o último passo e os códigos de
/// recuperação, troca o <c>security_stamp</c> e revoga as autorizações da pessoa. Sem a revogação,
/// a sessão morta pelo stamp apareceria viva por até 14 dias (design.md da change
/// rotas-conta-usuarios, D2). O login seguinte pede o cadastro.
/// </summary>
public sealed class TwoFactorReset(AppDbContext dbContext, UserSessions sessions)
{
    /// <summary>Redefine a verificação de <paramref name="target"/>; devolve quantas sessões foram encerradas.</summary>
    public async Task<int> ResetAsync(User target, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        target.ResetTwoFactor();
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.UserRecoveryCodes.Where(code => code.UserId == target.Id).ExecuteDeleteAsync(cancellationToken);
        var ended = await sessions.EndAllAsync(target.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ended;
    }
}
