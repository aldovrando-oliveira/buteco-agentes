namespace Buteco.Api.Users;

/// <summary>
/// O texto de <see cref="UserStatus"/> na coluna <c>users.status</c>. Texto desconhecido lança
/// (design.md da change convites-redefinicao-senha, D2): antes, todo texto diferente de
/// <c>active</c> virava <see cref="UserStatus.Deactivated"/>, e um valor novo seria lido como
/// desativado em silêncio. A conversão do EF é árvore de expressão e não aceita <c>throw</c>, por
/// isso a leitura passa por este método.
/// </summary>
public static class UserStatusText
{
    public const string Active = "active";
    public const string Deactivated = "deactivated";
    public const string Invited = "invited";

    public static string Write(UserStatus status) => status switch
    {
        UserStatus.Active => Active,
        UserStatus.Deactivated => Deactivated,
        UserStatus.Invited => Invited,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Estado de usuário sem texto."),
    };

    public static UserStatus Read(string value) => value switch
    {
        Active => UserStatus.Active,
        Deactivated => UserStatus.Deactivated,
        Invited => UserStatus.Invited,
        _ => throw new InvalidOperationException($"Estado de usuário desconhecido: '{value}'."),
    };
}
