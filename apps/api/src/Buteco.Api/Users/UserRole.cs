namespace Buteco.Api.Users;

/// <summary>
/// O papel do usuário local (design.md da change papeis-no-token, D3): Admin gere usuários,
/// convites e a identidade; Operador gere agentes, MCP, conhecimento e canais; Leitor só lê,
/// sem o histórico de conversas. A matriz por rota está nas tabelas <c>RoleRoutes</c> de cada
/// app. Gravado como texto (<see cref="UserRoleText"/>), nunca pelo número do membro: o primeiro
/// membro não é um padrão, e todo usuário é criado com o papel explícito (D4).
/// </summary>
public enum UserRole
{
    Admin = 1,
    Operator = 2,
    Reader = 3,
}
