namespace Buteco.Api.Users;

/// <summary>
/// Estado do usuário local (design.md da change usuarios-locais-login-emissor, D4). Um
/// usuário que já foi ativo não é excluído: ele é desativado. O convidado (design.md da change
/// convites-redefinicao-senha, D2) ainda não tem senha e não entra; aceitar o convite o põe
/// ativo, e cancelar o convite o apaga. Gravado como texto (<see cref="UserStatusText"/>).
/// </summary>
public enum UserStatus
{
    Active,
    Deactivated,
    Invited,
}
