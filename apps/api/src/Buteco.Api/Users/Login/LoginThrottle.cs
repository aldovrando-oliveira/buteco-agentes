namespace Buteco.Api.Users.Login;

/// <summary>
/// Uma linha de <c>identity_login_throttle</c> (design.md da change
/// usuarios-locais-login-emissor, D4). A chave é o e-mail normalizado, e não o usuário:
/// a conta inexistente bloqueia igual, e o aviso não serve para descobrir quem tem conta.
/// As regras são de <see cref="LoginThrottlePolicy"/>; a entidade só guarda o estado.
/// </summary>
public sealed class LoginThrottle
{
    public string NormalizedEmail { get; set; } = null!;

    /// <summary>Falhas seguidas antes do primeiro bloqueio.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Duração do último bloqueio, em minutos; 0 enquanto não houve bloqueio.</summary>
    public int LastLockMinutes { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset LastFailureAt { get; set; }

    internal LoginThrottleState ToState() => new(ConsecutiveFailures, LastLockMinutes, LockedUntil, LastFailureAt);

    internal void Apply(LoginThrottleState state)
    {
        ConsecutiveFailures = state.ConsecutiveFailures;
        LastLockMinutes = state.LastLockMinutes;
        LockedUntil = state.LockedUntil;
        LastFailureAt = state.LastFailureAt ?? LastFailureAt;
    }
}
