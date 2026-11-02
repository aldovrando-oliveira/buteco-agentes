using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Users.Sessions;

/// <summary>
/// As sessões de um usuário, sobre as tabelas do OpenIddict (design.md da change
/// rotas-conta-usuarios, D2 e D4).
///
/// <list type="bullet">
/// <item><b>Sessão viva</b> = autorização ad hoc <c>valid</c> do <c>subject</c>, com um refresh
/// <c>valid</c> e não vencido. A autorização de um login abandonado (código não trocado) ou de
/// uma sessão parada além do refresh continua <c>valid</c> até a limpeza, e não renova: não é
/// sessão;</item>
/// <item><b>último uso</b> = a maior <c>creation_date</c> dos tokens da autorização. O refresh é
/// rotativo, e cada renovação grava um novo na mesma autorização: não precisa de coluna. A data
/// do token sai do <c>iat</c>, em segundos, e a da autorização tem a fração: o último uso tem
/// piso na criação, para não sair antes dela;</item>
/// <item><b>encerrar é pôr <c>revoked</c> na autorização</b>, e o refresh dela deixa de ser
/// aceito. Um comando só, no <see cref="AppDbContext"/> do escopo, então dentro da transação
/// de quem chama.</item>
/// </list>
///
/// <para>
/// <b>O <c>UPDATE</c> é nosso, e não o <c>RevokeBySubjectAsync</c> do OpenIddict.</b> O do
/// store (7.7.2, <c>OpenIddictEntityFrameworkCoreAuthorizationStore.cs</c> l. 840–853) é
/// <c>UPDATE openiddict_authorizations SET "Status" = 'revoked' WHERE "Subject" = @s</c>. Este
/// acrescenta <c>"Status" = 'valid'</c>, para a contagem de <c>encerradas</c> ser a de sessões
/// que estavam abertas, e <c>"Id" &lt;&gt; @atual</c> no "encerrar as outras", que o OpenIddict
/// não tem. Numa atualização da biblioteca, reler aquelas linhas: se o store passar a fazer
/// mais que pôr <c>revoked</c>, este comando precisa acompanhar.
/// </para>
/// </summary>
public sealed class UserSessions(AppDbContext dbContext, TimeProvider timeProvider)
{
    public sealed record Session(string Id, DateTimeOffset CriadaEm, DateTimeOffset UltimoUso);

    public sealed record Summary(int Ativas, DateTimeOffset? UltimaAtividade);

    public async Task<IReadOnlyList<Session>> ListLiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await Live(userId)
            .OrderByDescending(authorization => authorization.CreationDate)
            .ThenBy(authorization => authorization.Id)
            .Select(authorization => new
            {
                authorization.Id,
                authorization.CreationDate,
                LastUse = authorization.Tokens.Max(token => token.CreationDate),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new Session(row.Id!, Utc(row.CreationDate), LastUse(row.CreationDate, row.LastUse))).ToList();
    }

    public async Task<Summary> SummarizeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await Live(userId)
            .Select(authorization => new { authorization.CreationDate, LastUse = authorization.Tokens.Max(token => token.CreationDate) })
            .ToListAsync(cancellationToken);

        return new Summary(rows.Count, rows.Count == 0 ? null : rows.Max(row => LastUse(row.CreationDate, row.LastUse)));
    }

    /// <summary>
    /// Encerra a sessão <paramref name="authorizationId"/> se ela for uma sessão viva de
    /// <paramref name="userId"/>. A de outra pessoa, a inexistente e a que já não renova dão
    /// <c>false</c>, sem revogar nada: a conferência do dono está no próprio <c>WHERE</c>.
    /// </summary>
    public async Task<bool> EndOneAsync(Guid userId, string authorizationId, CancellationToken cancellationToken) =>
        await Live(userId)
            .Where(authorization => authorization.Id == authorizationId)
            .ExecuteUpdateAsync(Revoke, cancellationToken) == 1;

    /// <summary>Todas as sessões abertas, menos a do token de quem chama.</summary>
    public Task<int> EndOthersAsync(Guid userId, string currentAuthorizationId, CancellationToken cancellationToken) =>
        Open(userId)
            .Where(authorization => authorization.Id != currentAuthorizationId)
            .ExecuteUpdateAsync(Revoke, cancellationToken);

    /// <summary>
    /// Todas. É o que acompanha toda troca do <c>security_stamp</c> (D2): sem isso, a sessão
    /// morta pelo stamp continuaria aparecendo viva por até 14 dias.
    /// </summary>
    public Task<int> EndAllAsync(Guid userId, CancellationToken cancellationToken) =>
        Open(userId).ExecuteUpdateAsync(Revoke, cancellationToken);

    private IQueryable<OpenIddictEntityFrameworkCoreAuthorization> Open(Guid userId)
    {
        var subject = userId.ToString();
        return dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>()
            .Where(authorization => authorization.Subject == subject && authorization.Status == Statuses.Valid);
    }

    private IQueryable<OpenIddictEntityFrameworkCoreAuthorization> Live(Guid userId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return Open(userId).Where(authorization =>
            authorization.Type == AuthorizationTypes.AdHoc &&
            authorization.Tokens.Any(token =>
                token.Type == TokenTypeIdentifiers.RefreshToken &&
                token.Status == Statuses.Valid &&
                token.ExpirationDate > now));
    }

    private static void Revoke(Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<OpenIddictEntityFrameworkCoreAuthorization> setters) =>
        setters.SetProperty(authorization => authorization.Status, Statuses.Revoked);

    private static DateTimeOffset LastUse(DateTime? creation, DateTime? lastToken)
    {
        var created = Utc(creation);
        return lastToken is null ? created : Max(created, Utc(lastToken));
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static DateTimeOffset Utc(DateTime? value) =>
        new(DateTime.SpecifyKind(value ?? throw new InvalidOperationException("Autorização sem data de criação."), DateTimeKind.Utc));
}
