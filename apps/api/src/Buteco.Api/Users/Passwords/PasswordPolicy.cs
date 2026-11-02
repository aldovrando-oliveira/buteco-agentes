namespace Buteco.Api.Users.Passwords;

/// <summary>
/// A regra da senha nova (design.md da change rotas-conta-usuarios, D3): de 12 a 128
/// caracteres pelo <see cref="string.Length"/>, sem <c>Trim</c> (espaço é caractere de senha) e
/// sem regra de composição. O mínimo é o do protótipo ("Mínimo de 12 caracteres"); o máximo
/// limita o custo do Argon2id a uma entrada de tamanho conhecido.
///
/// <para>
/// Vale só para a senha <b>nova</b>: a troca da própria senha e, depois, a criação de senha
/// do convite e da redefinição por link (#181). A senha já gravada continua aceita no login e
/// como senha atual, mesmo mais curta, como a do bootstrap.
/// </para>
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    public const int MaximumLength = 128;

    public const string Message = "A senha precisa ter de 12 a 128 caracteres.";

    public static bool IsSatisfiedBy(string? password) =>
        password is { Length: >= MinimumLength and <= MaximumLength };
}
