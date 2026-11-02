namespace Buteco.Api.Users;

/// <summary>
/// Usuário local do <c>apps/api</c> (design.md da change usuarios-locais-login-emissor,
/// D4). O <see cref="Id"/> é imutável e é o <c>sub</c> dos tokens; o e-mail é só o
/// identificador de login, e trocá-lo (<see cref="UpdateProfile"/>) não muda o <c>sub</c>.
///
/// <para>
/// <see cref="SecurityStamp"/> vai no cookie de sessão do emissor, no código e no refresh
/// token. Ele muda ao desativar (<see cref="Deactivate"/>), ao trocar a senha
/// (<see cref="ChangePassword"/>) e ao redefinir as 2 etapas (<see cref="ResetTwoFactor"/>), e
/// todo token novo de usuário confere o valor gravado (D4). Quem troca o stamp revoga também
/// as autorizações da pessoa, na mesma transação (<c>UserSessions.EndAllAsync</c>; design.md
/// da change rotas-conta-usuarios, D2).
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

    /// <summary>O segredo do TOTP cifrado (design.md da change verificacao-2-etapas-totp, D5); nulo sem a verificação.</summary>
    public byte[]? TwoFactorSecret { get; private set; }

    /// <summary>Quando a verificação foi ligada; nulo = desligada.</summary>
    public DateTimeOffset? TwoFactorEnabledAt { get; private set; }

    /// <summary>
    /// O último passo do TOTP aceito (D3). Quem aceita um código grava o passo por
    /// <c>UPDATE</c> condicional, e não por esta entidade (<c>TwoFactorVerifier</c>).
    /// </summary>
    public long? TwoFactorLastStep { get; private set; }

    /// <summary>O segredo cifrado de um cadastro em curso, até a confirmação (D8).</summary>
    public byte[]? TwoFactorPendingSecret { get; private set; }

    public DateTimeOffset? TwoFactorPendingAt { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    public bool IsTwoFactorEnabled => TwoFactorEnabledAt is not null;

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

    /// <summary>
    /// A troca da própria senha (design.md da change rotas-conta-usuarios, D3): o hash novo, sem
    /// o marcador de legado, e o stamp novo, que derruba toda renovação de antes.
    /// </summary>
    public void ChangePassword(string passwordHash)
    {
        ReplacePasswordHash(passwordHash);
        SecurityStamp = NewSecurityStamp();
    }

    /// <summary>
    /// Nome e e-mail, sem mudar o <c>sub</c> nem o stamp (D5): o e-mail é o identificador do
    /// login, e não um segredo, e as sessões abertas continuam sendo da mesma pessoa.
    /// </summary>
    public void UpdateProfile(string name, string email)
    {
        Name = name.Trim();
        Email = email.Trim();
        NormalizedEmail = EmailNormalization.Normalize(email);
    }

    public void RecordAccess(DateTimeOffset now) => LastAccessAt = now;

    /// <summary>
    /// Desativa e troca o stamp: o cookie em voo e todo código ou refresh emitido antes
    /// deixam de valer, e a reativação (<see cref="Activate"/>) não os traz de volta.
    /// </summary>
    public void Deactivate(DateTimeOffset now)
    {
        Status = UserStatus.Deactivated;
        DeactivatedAt = now;
        SecurityStamp = NewSecurityStamp();
    }

    /// <summary>
    /// Reativa sem trocar o stamp: o da desativação já invalidou tudo o que havia antes, e a
    /// pessoa entra de novo com a mesma senha (design.md da change rotas-conta-usuarios, D7).
    /// </summary>
    public void Activate()
    {
        Status = UserStatus.Active;
        DeactivatedAt = null;
    }

    /// <summary>Um cadastro novo substitui o anterior, se houver (D8).</summary>
    public void StartTwoFactorEnrollment(byte[] protectedSecret, DateTimeOffset now)
    {
        TwoFactorPendingSecret = protectedSecret;
        TwoFactorPendingAt = now;
    }

    /// <summary>
    /// Liga a verificação com o segredo pendente, confirmado pelo código do passo
    /// <paramref name="confirmedStep"/>, que fica gravado: o mesmo código não serve para entrar.
    /// Não troca o stamp: as outras sessões já eram da pessoa.
    /// </summary>
    public void EnableTwoFactor(DateTimeOffset now, long confirmedStep)
    {
        TwoFactorSecret = TwoFactorPendingSecret ?? throw new InvalidOperationException("Sem cadastro pendente.");
        TwoFactorEnabledAt = now;
        TwoFactorLastStep = confirmedStep;
        TwoFactorPendingSecret = null;
        TwoFactorPendingAt = null;
    }

    /// <summary>A remoção pela própria pessoa, com um código (D8). Não troca o stamp.</summary>
    public void RemoveTwoFactor()
    {
        TwoFactorSecret = null;
        TwoFactorEnabledAt = null;
        TwoFactorLastStep = null;
        TwoFactorPendingSecret = null;
        TwoFactorPendingAt = null;
    }

    /// <summary>
    /// A redefinição pelo admin (D9): apaga como a remoção e troca o stamp, e o cookie em voo e
    /// toda renovação de antes deixam de valer.
    /// </summary>
    public void ResetTwoFactor()
    {
        RemoveTwoFactor();
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
