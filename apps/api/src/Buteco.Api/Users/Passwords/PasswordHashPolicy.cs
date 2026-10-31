namespace Buteco.Api.Users.Passwords;

/// <summary>
/// Parâmetros do Argon2id (design.md da change usuarios-locais-login-emissor, D4): o mínimo
/// da OWASP, m = 19.456 KiB, t = 2, p = 1. Constantes, e não opção (convenção 2). Eles vão
/// dentro da string PHC, e um hash gravado com outros parâmetros é refeito no login
/// seguinte (<see cref="PasswordVerificationResult.SucceededNeedsRehash"/>).
/// </summary>
public static class PasswordHashPolicy
{
    public const int MemorySizeKiB = 19456;

    public const int NumberOfPasses = 2;

    /// <summary>A libsodium só aceita 1 (<c>Argon2id.cs</c> do NSec 26.4.0, l. 87–90).</summary>
    public const int DegreeOfParallelism = 1;

    /// <summary>A libsodium só aceita 16 (<c>crypto_pwhash_argon2id_SALTBYTES</c>).</summary>
    public const int SaltSize = 16;

    public const int HashSize = 32;
}
