using System.Security.Cryptography;
using System.Text;

namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// O token do link de convite e de redefinição (design.md da change convites-redefinicao-senha,
/// D4): 32 bytes de <see cref="RandomNumberGenerator"/> em base64url sem preenchimento (43
/// caracteres), guardado só como SHA-256, como os códigos de recuperação da #218. Hash rápido, e
/// não Argon2: 256 bits de entropia não têm dicionário a atrasar, e a busca precisa ser
/// determinística para usar o índice. A validade é constante, e não opção (convenção 2).
/// </summary>
public static class AccessLinkToken
{
    public const int SizeInBytes = 32;

    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(72);

    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(24);

    public static string New() => Base64UrlEncode(RandomNumberGenerator.GetBytes(SizeInBytes));

    /// <summary>O SHA-256 do texto do token, como chega no corpo: é o que se grava e se procura.</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    public static TimeSpan LifetimeOf(AccessLinkPurpose purpose) => purpose switch
    {
        AccessLinkPurpose.Invitation => InvitationLifetime,
        AccessLinkPurpose.PasswordReset => PasswordResetLifetime,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
    };

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
