using Buteco.Api.Auth;

namespace Buteco.Api.Users.Passwords;

/// <summary>
/// O PBKDF2-HMAC-SHA256 de <c>Auth:OperatorPasswordHash</c>, no formato
/// <c>{iterações}.{sal}.{hash}</c> (design.md da change usuarios-locais-login-emissor, D2).
/// Só o <see cref="PasswordHasher"/> o chama, e só para a linha com o marcador de legado
/// de bootstrap: qualquer outra linha com esse formato reprova como hash inválido.
/// </summary>
public static class BootstrapLegacyPasswordVerifier
{
    /// <summary>A forma, sem verificar senha: o bootstrap só cria o usuário com um hash que parseia.</summary>
    public static bool IsWellFormed(string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('.', 3);
        return parts.Length == 3 &&
            int.TryParse(parts[0], out var iterations) && iterations > 0 &&
            IsBase64(parts[1]) && IsBase64(parts[2]);
    }

    public static bool Verify(string password, string storedHash) =>
        IsWellFormed(storedHash) && OperatorPasswordHasher.Verify(password, storedHash);

    private static bool IsBase64(string value) =>
        value.Length > 0 && Convert.TryFromBase64String(value, new byte[value.Length], out var written) && written > 0;
}
