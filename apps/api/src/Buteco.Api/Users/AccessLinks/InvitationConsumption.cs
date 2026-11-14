namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// A condição de consumo do convite, uma só para os dois aceites (design.md da change aceite-convite-google,
/// D2 e D4): o id, o hash do link, o propósito <c>invitation</c>, o vencimento no futuro e o estado convidado.
/// O aceite por senha e o pelo provedor montam o <c>WHERE</c> do <c>UPDATE</c> único por aqui, e o mesmo convite
/// nunca é aceito pelos dois: no <c>READ COMMITTED</c>, o segundo <c>UPDATE</c> espera a trava da linha,
/// reavalia a condição sobre a versão nova e volta zero linhas.
/// </summary>
public static class InvitationConsumption
{
    public static IQueryable<User> Pending(IQueryable<User> users, Guid userId, byte[] linkHash, DateTimeOffset now) =>
        users.Where(user => user.Id == userId &&
            user.AccessLinkHash == linkHash &&
            user.AccessLinkPurpose == AccessLinkPurpose.Invitation &&
            user.AccessLinkExpiresAt > now &&
            user.Status == UserStatus.Invited);
}
