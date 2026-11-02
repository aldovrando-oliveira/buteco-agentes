using Buteco.Api.Users.Sessions;

namespace Buteco.Api.Users.Management;

/// <summary>
/// Uma linha da lista de Usuários (design.md da change rotas-conta-usuarios, D6).
/// <c>FormasDeEntrada</c> é <c>["senha"]</c> quando há senha gravada: na M1 é a única forma, e a
/// coluna do protótipo já existe; a M3 acrescenta os provedores.
/// </summary>
public sealed record UserSummary(
    Guid Id,
    string Nome,
    string Email,
    string Estado,
    IReadOnlyList<string> FormasDeEntrada,
    bool DuasEtapas,
    DateTimeOffset? UltimoAcesso,
    DateTimeOffset CriadoEm)
{
    public const string ActiveState = "ativo";

    public const string DeactivatedState = "desativado";

    public const string PasswordSignIn = "senha";

    public static UserSummary From(User user) => new(
        user.Id,
        user.Name,
        user.Email,
        StateOf(user.Status),
        SignInFormsOf(user.PasswordHash is not null),
        user.IsTwoFactorEnabled,
        user.LastAccessAt,
        user.CreatedAt);

    public static string StateOf(UserStatus status) => status == UserStatus.Active ? ActiveState : DeactivatedState;

    public static IReadOnlyList<string> SignInFormsOf(bool hasPassword) => hasPassword ? [PasswordSignIn] : [];
}

/// <summary>
/// O detalhe de Editar usuário: a linha da lista, a data da desativação e a contagem das sessões
/// vivas com a última atividade, que é o que o quadro mostra (D4). A lista das sessões de outra
/// pessoa não existe: nenhum quadro a pede.
/// </summary>
public sealed record UserDetail(
    Guid Id,
    string Nome,
    string Email,
    string Estado,
    IReadOnlyList<string> FormasDeEntrada,
    bool DuasEtapas,
    DateTimeOffset? UltimoAcesso,
    DateTimeOffset CriadoEm,
    DateTimeOffset? DesativadoEm,
    UserSessionsSummary Sessoes)
{
    public static UserDetail From(User user, UserSessions.Summary sessions)
    {
        var summary = UserSummary.From(user);
        return new(
            summary.Id, summary.Nome, summary.Email, summary.Estado, summary.FormasDeEntrada, summary.DuasEtapas,
            summary.UltimoAcesso, summary.CriadoEm, user.DeactivatedAt, new UserSessionsSummary(sessions.Ativas, sessions.UltimaAtividade));
    }
}

public sealed record UserSessionsSummary(int Ativas, DateTimeOffset? UltimaAtividade);

public sealed record UpdateUserRequest(string? Nome, string? Email);

public sealed record EndedSessionsResponse(int Encerradas);
