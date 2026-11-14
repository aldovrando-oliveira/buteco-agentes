using Buteco.Api.Users.Federated;
using Buteco.Api.Users.Sessions;

namespace Buteco.Api.Users.Management;

/// <summary>
/// Uma linha da lista de Usuários (design.md da change rotas-conta-usuarios, D6).
/// <c>FormasDeEntrada</c> traz <c>"senha"</c> quando há senha gravada e, desde a M3, o tipo de cada
/// provedor disponível com vínculo (<c>"google"</c>), no formato de strings que o painel já lê
/// (<see cref="FederatedSignInForms"/>; design.md da change provedores-federados-google, D9). O convidado (design.md da change
/// convites-redefinicao-senha, D10) não tem senha, e traz <c>Convite</c> com o vencimento; o
/// <c>Vencido</c> sai do relógio do servidor, para a tela não afirmar pelo relógio do navegador.
/// <c>Papel</c> é o gravado, em <c>admin</c>, <c>operador</c> ou <c>leitor</c>
/// (<see cref="UserRoleText.WriteWire"/>; design.md da change papeis-no-token, D3), também no
/// convidado, com o papel do convite.
/// </summary>
public sealed record UserSummary(
    Guid Id,
    string Nome,
    string Email,
    string Estado,
    string Papel,
    IReadOnlyList<string> FormasDeEntrada,
    bool DuasEtapas,
    DateTimeOffset? UltimoAcesso,
    DateTimeOffset CriadoEm,
    InvitationSummary? Convite)
{
    public const string ActiveState = "ativo";

    public const string DeactivatedState = "desativado";

    public const string InvitedState = "convidado";

    public const string PasswordSignIn = "senha";

    public static UserSummary From(User user, DateTimeOffset now, IReadOnlyList<string>? signInForms = null) => new(
        user.Id,
        user.Name,
        user.Email,
        StateOf(user.Status),
        UserRoleText.WriteWire(user.Role),
        signInForms ?? SignInFormsOf(user.PasswordHash is not null),
        user.IsTwoFactorEnabled,
        user.LastAccessAt,
        user.CreatedAt,
        InvitationOf(user, now));

    public static string StateOf(UserStatus status) => status switch
    {
        UserStatus.Active => ActiveState,
        UserStatus.Deactivated => DeactivatedState,
        UserStatus.Invited => InvitedState,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static IReadOnlyList<string> SignInFormsOf(bool hasPassword) => hasPassword ? [PasswordSignIn] : [];

    // Só o convidado tem convite. O link de convite do convidado nunca é apagado sem a linha: o
    // vencido continua até gerar novo link ou cancelar (D5).
    private static InvitationSummary? InvitationOf(User user, DateTimeOffset now) =>
        user is { IsInvited: true, AccessLinkExpiresAt: { } expiresAt }
            ? new InvitationSummary(expiresAt, expiresAt <= now)
            : null;
}

public sealed record InvitationSummary(DateTimeOffset ExpiraEm, bool Vencido);

/// <summary>Quem convidou, para o "Convidado por … em …" (o <c>CriadoEm</c> é a data do convite).</summary>
public sealed record UserReference(Guid Id, string Nome);

/// <summary>
/// O detalhe de Editar usuário: a linha da lista, a data da desativação, quem convidou e a
/// contagem das sessões vivas com a última atividade, que é o que o quadro mostra (D4). A lista
/// das sessões de outra pessoa não existe: nenhum quadro a pede.
/// </summary>
public sealed record UserDetail(
    Guid Id,
    string Nome,
    string Email,
    string Estado,
    string Papel,
    IReadOnlyList<string> FormasDeEntrada,
    bool DuasEtapas,
    DateTimeOffset? UltimoAcesso,
    DateTimeOffset CriadoEm,
    InvitationSummary? Convite,
    DateTimeOffset? DesativadoEm,
    UserReference? ConvidadoPor,
    UserSessionsSummary Sessoes,
    IReadOnlyList<ConnectedAccount> ContasConectadas)
{
    public static UserDetail From(
        User user, UserReference? invitedBy, UserSessions.Summary sessions, DateTimeOffset now,
        IReadOnlyList<string> signInForms, IReadOnlyList<ConnectedAccount> connected)
    {
        var summary = UserSummary.From(user, now, signInForms);
        return new(
            summary.Id, summary.Nome, summary.Email, summary.Estado, summary.Papel, summary.FormasDeEntrada, summary.DuasEtapas,
            summary.UltimoAcesso, summary.CriadoEm, summary.Convite, user.DeactivatedAt, invitedBy,
            new UserSessionsSummary(sessions.Ativas, sessions.UltimaAtividade), connected);
    }
}

public sealed record UserSessionsSummary(int Ativas, DateTimeOffset? UltimaAtividade);

public sealed record UpdateUserRequest(string? Nome, string? Email);

public sealed record EndedSessionsResponse(int Encerradas);
