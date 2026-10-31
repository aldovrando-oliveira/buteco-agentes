namespace Buteco.Api.Users;

/// <summary>
/// Usuário local do <c>apps/api</c> (design.md da change usuarios-locais-login-emissor,
/// D4). O <see cref="Id"/> é imutável e é o <c>sub</c> dos tokens; o e-mail é só o
/// identificador de login, e trocá-lo (#219) não muda o <c>sub</c>.
///
/// <para>
/// <see cref="SecurityStamp"/> vai no cookie de sessão do emissor, no código e no refresh
/// token. Ele muda ao desativar, ao trocar a senha (#219) e ao redefinir as 2 etapas
/// (#218), e todo token novo de usuário confere o valor gravado (D4).
/// </para>
/// </summary>
public sealed class User
{
    private User()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Como a pessoa digitou, sem espaços nas pontas.</summary>
    public string Email { get; private set; } = null!;

    /// <summary><see cref="EmailNormalization.Normalize"/> do e-mail; índice único.</summary>
    public string NormalizedEmail { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public UserStatus Status { get; private set; }

    /// <summary>
    /// String PHC do Argon2id ou, só com <see cref="PasswordHashIsBootstrapLegacy"/>, o
    /// PBKDF2 de <c>Auth:OperatorPasswordHash</c>. Nula para quem não tem senha (convite
    /// pendente na #181, só federado na M3).
    /// </summary>
    public string? PasswordHash { get; private set; }

    /// <summary>Verdadeiro só na linha do bootstrap, até o primeiro login (D2).</summary>
    public bool PasswordHashIsBootstrapLegacy { get; private set; }

    public string SecurityStamp { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastAccessAt { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    public static User Create(string email, string name, string? passwordHash, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Email = email.Trim(),
        NormalizedEmail = EmailNormalization.Normalize(email),
        Name = name,
        Status = UserStatus.Active,
        PasswordHash = passwordHash,
        SecurityStamp = NewSecurityStamp(),
        CreatedAt = now,
    };

    /// <summary>O primeiro Admin, com o hash PBKDF2 da configuração marcado como legado (D2).</summary>
    public static User CreateBootstrap(string email, string legacyPasswordHash, DateTimeOffset now)
    {
        var user = Create(email, NameFromEmail(email), legacyPasswordHash, now);
        user.PasswordHashIsBootstrapLegacy = true;
        return user;
    }

    /// <summary>
    /// Troca o hash depois de um login bem-sucedido que pediu refazê-lo (legado ou
    /// parâmetros antigos). Não troca o stamp: a senha é a mesma, só muda a forma de
    /// guardá-la.
    /// </summary>
    public void ReplacePasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        PasswordHashIsBootstrapLegacy = false;
    }

    public void RecordAccess(DateTimeOffset now) => LastAccessAt = now;

    /// <summary>
    /// Desativa e troca o stamp: o cookie em voo e todo código ou refresh emitido antes
    /// deixam de valer, e uma reativação (#219) não os traz de volta.
    /// </summary>
    public void Deactivate(DateTimeOffset now)
    {
        Status = UserStatus.Deactivated;
        DeactivatedAt = now;
        SecurityStamp = NewSecurityStamp();
    }

    private static string NameFromEmail(string email)
    {
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        return at > 0 ? trimmed[..at] : trimmed;
    }

    private static string NewSecurityStamp() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
}
