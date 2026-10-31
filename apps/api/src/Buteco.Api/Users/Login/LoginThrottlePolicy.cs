namespace Buteco.Api.Users.Login;

/// <summary>O estado de um e-mail, sem banco, para a política decidir.</summary>
public readonly record struct LoginThrottleState(
    int ConsecutiveFailures,
    int LastLockMinutes,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastFailureAt)
{
    public static readonly LoginThrottleState Empty = new(0, 0, null, null);
}

/// <summary>
/// O bloqueio progressivo por e-mail (design.md da change usuarios-locais-login-emissor,
/// D4, decisões do dono de 28 e 31/10/2026). Constantes, e não opção (convenção 2):
///
/// <list type="bullet">
/// <item><b>primeiro bloqueio:</b> 5 falhas seguidas → 5 min;</item>
/// <item><b>reincidência:</b> depois de um bloqueio vencido, <b>uma</b> falha bloqueia pelo
/// dobro do último, até 60 min;</item>
/// <item><b>volta ao início:</b> com a última falha há mais de 24 h, o estado vale como
/// vazio. A regra é pelo tempo, e não pela limpeza: o resultado não depende de quando o
/// ciclo horário remove a linha;</item>
/// <item><b>durante o bloqueio</b> a tentativa não conta nem estende, e quem decide isso é
/// a rota, que nem chega a verificar a senha;</item>
/// <item><b>sucesso</b> zera tudo, e o desativado com a senha certa não toca no estado
/// (os dois são da rota).</item>
/// </list>
/// </summary>
public static class LoginThrottlePolicy
{
    public const int FailuresBeforeFirstLock = 5;

    public static readonly TimeSpan FirstLock = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MaxLock = TimeSpan.FromMinutes(60);

    /// <summary>Sem falha por este tempo, o e-mail volta ao início. É também a idade da limpeza.</summary>
    public static readonly TimeSpan ResetAfter = TimeSpan.FromHours(24);

    /// <summary>O estado que vale agora: vazio quando a última falha passou de <see cref="ResetAfter"/>.</summary>
    public static LoginThrottleState Effective(LoginThrottleState state, DateTimeOffset now) =>
        state.LastFailureAt is { } last && now - last <= ResetAfter ? state : LoginThrottleState.Empty;

    /// <summary>Quanto falta do bloqueio, ou nulo fora dele.</summary>
    public static TimeSpan? LockRemaining(LoginThrottleState state, DateTimeOffset now) =>
        Effective(state, now).LockedUntil is { } until && until > now ? until - now : null;

    /// <summary>O estado depois de uma falha fora de bloqueio.</summary>
    public static LoginThrottleState RecordFailure(LoginThrottleState state, DateTimeOffset now)
    {
        var current = Effective(state, now);

        if (current.LastLockMinutes == 0)
        {
            var failures = current.ConsecutiveFailures + 1;
            return failures >= FailuresBeforeFirstLock
                ? new LoginThrottleState(0, (int)FirstLock.TotalMinutes, now + FirstLock, now)
                : current with { ConsecutiveFailures = failures, LastFailureAt = now };
        }

        var next = Math.Min(current.LastLockMinutes * 2, (int)MaxLock.TotalMinutes);
        return new LoginThrottleState(0, next, now + TimeSpan.FromMinutes(next), now);
    }
}
