namespace Buteco.Api.Users;

/// <summary>
/// O identificador de login (D4): o e-mail sem espaços nas pontas e em minúsculas. É a
/// coluna de índice único de <c>users</c> e a chave de <c>identity_login_throttle</c>.
/// </summary>
public static class EmailNormalization
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
