namespace Buteco.Api.Users.Federated;

/// <summary>
/// O vínculo entre um usuário e uma conta de um provedor federado (design.md da change
/// provedores-federados-google, D6): o par (<see cref="Issuer"/>, <see cref="Subject"/>) é a identidade
/// do provedor, e o login acha o usuário só por ele, nunca pelo e-mail. <see cref="Email"/> é o último
/// e-mail que o provedor confirmou, só para exibição. O issuer é o fixo do tipo, nunca o lido do token.
/// </summary>
public sealed class UserFederatedIdentity
{
    private UserFederatedIdentity()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Issuer { get; private set; } = null!;

    public string Subject { get; private set; } = null!;

    public string? Email { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public static UserFederatedIdentity Connect(Guid userId, string issuer, string subject, string? email, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        UserId = userId,
        Issuer = issuer,
        Subject = subject,
        Email = email,
        ConnectedAt = now,
    };

    /// <summary>O login aceito regrava o e-mail confirmado e o último uso; o e-mail do usuário não muda.</summary>
    public void RecordUse(string? email, DateTimeOffset now)
    {
        Email = email;
        LastUsedAt = now;
    }
}
