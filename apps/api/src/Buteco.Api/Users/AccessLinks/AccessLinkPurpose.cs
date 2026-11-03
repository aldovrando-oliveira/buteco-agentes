namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// Para que serve o link pendente de um usuário (design.md da change
/// convites-redefinicao-senha, D4). Um convidado nunca tem redefinição (não tem senha), e um
/// ativo nunca tem convite, então um lugar só na linha basta. Gravado como texto
/// (<see cref="Write"/>, <see cref="Read"/>); texto desconhecido lança, como o estado.
/// </summary>
public enum AccessLinkPurpose
{
    Invitation,
    PasswordReset,
}

public static class AccessLinkPurposeText
{
    public const string Invitation = "invitation";
    public const string PasswordReset = "password_reset";

    public static string Write(AccessLinkPurpose purpose) => purpose switch
    {
        AccessLinkPurpose.Invitation => Invitation,
        AccessLinkPurpose.PasswordReset => PasswordReset,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Propósito de link sem texto."),
    };

    public static AccessLinkPurpose Read(string value) => value switch
    {
        Invitation => AccessLinkPurpose.Invitation,
        PasswordReset => AccessLinkPurpose.PasswordReset,
        _ => throw new InvalidOperationException($"Propósito de link desconhecido: '{value}'."),
    };
}
