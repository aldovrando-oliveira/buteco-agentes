namespace Buteco.Api.Users;

/// <summary>
/// Estado do usuário local (design.md da change usuarios-locais-login-emissor, D4). Um
/// usuário não é excluído: ele é desativado. Gravado como texto (<c>active</c>,
/// <c>deactivated</c>) pela conversão do <c>AppDbContext</c>.
/// </summary>
public enum UserStatus
{
    Active,
    Deactivated,
}
