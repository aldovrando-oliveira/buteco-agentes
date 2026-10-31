using Buteco.Api.Auth;

namespace Buteco.Api.Users.Bootstrap;

/// <summary>
/// O que o bootstrap do primeiro Admin lê (design.md da change
/// usuarios-locais-login-emissor, D2), da mesma seção <c>Auth</c> do login de hoje.
/// <c>Auth:OperatorPasswordHash</c> é o mesmo valor do <see cref="OperatorCredentialOptions"/>:
/// até a #217 ele serve aos dois, e depois só a este.
/// </summary>
public sealed class UserBootstrapOptions
{
    public const string SectionName = OperatorCredentialOptions.SectionName;

    /// <summary>Opcional: vazio, o bootstrap só avisa quando não há usuário.</summary>
    public string BootstrapAdminEmail { get; set; } = "";

    public string OperatorPasswordHash { get; set; } = "";
}
