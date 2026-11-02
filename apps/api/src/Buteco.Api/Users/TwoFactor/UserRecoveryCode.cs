namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// Um código de recuperação, guardado só como SHA-256 do normalizado (design.md da change
/// verificacao-2-etapas-totp, D6 e D11). O uso é marcado por <c>UPDATE</c> condicional em
/// <c>used_at</c>, e não por esta entidade.
/// </summary>
public sealed class UserRecoveryCode
{
    private UserRecoveryCode()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] CodeHash { get; private set; } = null!;

    public DateTimeOffset? UsedAt { get; private set; }

    public static UserRecoveryCode Create(Guid userId, string code, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        UserId = userId,
        CodeHash = RecoveryCode.Hash(RecoveryCode.Normalize(code) ?? throw new ArgumentException("Código fora da forma.", nameof(code))),
    };
}
