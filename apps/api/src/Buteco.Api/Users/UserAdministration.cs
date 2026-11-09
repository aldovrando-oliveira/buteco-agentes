using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users;

/// <summary>
/// O lock da desativação e da mudança de papel (design.md das changes rotas-conta-usuarios, D7, e
/// papeis-no-token, D6). A regra é "ninguém
/// desativa a si mesmo", e dela decorre "sempre sobra um ativo": quem chama está ativo e é
/// outra pessoa. Mas só se quem chama ainda estiver ativo quando grava. Sem o lock, os dois
/// únicos ativos desativando um ao outro ao mesmo tempo passariam cada um pela conferência do
/// outro ainda ativo, e ninguém sobraria. Com ele, a segunda desativação espera a primeira
/// confirmar e relê quem chama já desativado.
/// </summary>
public static class UserAdministration
{
    /// <summary>
    /// Chave do <c>pg_advisory_xact_lock</c> da desativação. São os bytes ASCII de
    /// <c>BUTECOD1</c> lidos como inteiro big-endian, na forma de uma chave <c>bigint</c>,
    /// pelo mesmo motivo da <c>IdentityKeyRotationPolicy.AdvisoryLockKey</c>
    /// (<c>BUTECOK1</c>): o espaço de 64 bits não se sobrepõe ao de dois <c>int4</c> do
    /// <c>apps/workers</c>. As outras siglas em uso no <c>apps/api</c> são <c>BUTECOC1</c>
    /// (catálogo de clientes), <c>BUTECOK1</c> (chaves do emissor), <c>BUTECOP1</c> (limpeza
    /// dos tokens) e <c>BUTECOU1</c> (bootstrap); o <c>AdvisoryLockKeysTests</c> confere que
    /// são distintas.
    /// </summary>
    public const long AdvisoryLockKey = 4779819235982394417;

    /// <summary>
    /// Quem chama, relido dentro do lock: ativo <b>e</b> Admin. Só um Admin desativa ou muda papel, e
    /// ele não desativa a si mesmo; relido assim, sobra sempre ele (design.md da change
    /// papeis-no-token, D6).
    /// </summary>
    public static Task<bool> IsActiveAdminAsync(AppDbContext dbContext, Guid callerId, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Id == callerId && user.Status == UserStatus.Active && user.Role == UserRole.Admin, cancellationToken);

    /// <summary>Os Admins ativos, contados dentro do lock: a regra do último Admin (D6).</summary>
    public static Task<int> CountActiveAdminsAsync(AppDbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Users.CountAsync(user => user.Status == UserStatus.Active && user.Role == UserRole.Admin, cancellationToken);

    /// <summary>Pega o lock na transação aberta em <paramref name="dbContext"/>; ele sai no commit ou no rollback.</summary>
    public static Task LockAsync(AppDbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);
}
