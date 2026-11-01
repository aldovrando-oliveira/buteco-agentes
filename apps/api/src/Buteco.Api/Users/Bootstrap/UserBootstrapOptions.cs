namespace Buteco.Api.Users.Bootstrap;

/// <summary>
/// O que o bootstrap do primeiro Admin lê (design.md da change
/// usuarios-locais-login-emissor, D2), da seção <c>Auth</c>. <c>Auth:OperatorPasswordHash</c>
/// era também a senha do <c>POST /auth/login</c>, que saiu na change
/// painel-entra-pelo-emissor (D8); desde então serve só ao bootstrap, e o nome e o
/// formato dela estão na #225 (D9).
/// </summary>
public sealed class UserBootstrapOptions
{
    public const string SectionName = "Auth";

    /// <summary>Opcional: vazio, o bootstrap só avisa quando não há usuário.</summary>
    public string BootstrapAdminEmail { get; set; } = "";

    public string OperatorPasswordHash { get; set; } = "";
}
