using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.Management;

/// <summary>
/// Usuários, sob <c>/users</c> (design.md da change rotas-conta-usuarios, D1 e D5 a D8). Na M1,
/// sem papéis, todo usuário real chama todas (<see cref="CurrentUser"/>), e o token de operador
/// cujo <c>sub</c> não é usuário recebe <c>403</c>. A #184 as restringe ao Admin. A redefinição das 2 etapas, no mesmo
/// prefixo, é da <c>UserTwoFactorResetEndpoint</c>.
///
/// <list type="bullet">
/// <item><b>Editar</b> não troca o stamp nem pede senha: o e-mail é o identificador do login,
/// e não um segredo, e na M1 nada é enviado ao endereço cadastrado (D5, com o gatilho para
/// voltar registrado na #181). O <c>409</c> vale também na corrida, pelo índice único;</item>
/// <item><b>desativar</b> troca o stamp e revoga as sessões, sob o lock
/// <see cref="UserAdministration.AdvisoryLockKey"/>, relendo quem chama dentro dele (D7);</item>
/// <item><b>desativar e reativar são idempotentes</b>, como em <c>McpServers</c>: o estado já
/// pedido responde <c>200</c> sem gravar nada;</item>
/// <item><b>o convidado</b> não é desativado nem reativado (<c>409</c>): ele sai pelo
/// cancelamento do convite e fica ativo pelo aceite (design.md da change
/// convites-redefinicao-senha, D2).</item>
/// </list>
/// </summary>
public static class UserEndpoints
{
    public const string GroupPath = "/users";

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPath);
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync);
        group.MapPost("/{id:guid}/activate", ActivateAsync);
        group.MapPost("/{id:guid}/sessions/revoke", RevokeSessionsAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext, AppDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        var users = await dbContext.Users.AsNoTracking()
            .OrderBy(user => user.Name)
            .ThenBy(user => user.Id)
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        return TypedResults.Ok(users.Select(user => UserSummary.From(user, now)).ToList());
    }

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, UserSessions sessions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        return await dbContext.Users.FindAsync([id], cancellationToken) is { } user
            ? await DetailAsync(user, dbContext, sessions, timeProvider, cancellationToken)
            : NotFound();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        UserSessions sessions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (UserFields.Validate(request.Nome, request.Email, out var name, out var email) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await dbContext.Users.FindAsync([id], cancellationToken) is not { } user)
        {
            return NotFound();
        }

        var normalized = EmailNormalization.Normalize(email);
        if (await dbContext.Users.AnyAsync(other => other.Id != id && other.NormalizedEmail == normalized, cancellationToken))
        {
            return EmailInUse();
        }

        user.UpdateProfile(name, email);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (UserFields.IsNormalizedEmailUniqueViolation(exception))
        {
            // Outra edição gravou o mesmo e-mail entre a conferência e esta escrita.
            return EmailInUse();
        }

        return await DetailAsync(user, dbContext, sessions, timeProvider, cancellationToken);
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        HttpContext httpContext,
        AppDbContext dbContext,
        UserSessions sessions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        if (caller.Id == id)
        {
            return TypedResults.Problem(title: "Você não pode desativar o próprio usuário.", statusCode: StatusCodes.Status403Forbidden);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await UserAdministration.LockAsync(dbContext, cancellationToken);

        // Dentro do lock: quem chama pode ter sido desativado por uma desativação que acabou de
        // confirmar. Sem esta releitura, os dois únicos ativos se desativariam um ao outro.
        if (!await dbContext.Users.AnyAsync(user => user.Id == caller.Id && user.Status == UserStatus.Active, cancellationToken))
        {
            return CurrentUser.Forbidden();
        }

        if (await dbContext.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken) is not { } target)
        {
            return NotFound();
        }

        // O convite sai pelo cancelamento, que invalida o link (design.md da change
        // convites-redefinicao-senha, D2): desativar o deixaria fora da lista de pendentes.
        if (target.IsInvited)
        {
            return NotYetAccepted();
        }

        if (target.IsActive)
        {
            target.Deactivate(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            await sessions.EndAllAsync(target.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(target, dbContext, sessions, timeProvider, cancellationToken);
    }

    private static async Task<IResult> ActivateAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, UserSessions sessions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (await dbContext.Users.FindAsync([id], cancellationToken) is not { } user)
        {
            return NotFound();
        }

        // O convidado só fica ativo pelo aceite, que grava a senha (design.md da change
        // convites-redefinicao-senha, D2): reativá-lo o deixaria ativo e sem senha.
        if (user.IsInvited)
        {
            return NotYetAccepted();
        }

        if (!user.IsActive)
        {
            user.Activate();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await DetailAsync(user, dbContext, sessions, timeProvider, cancellationToken);
    }

    private static async Task<IResult> RevokeSessionsAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, UserSessions sessions, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (!await dbContext.Users.AnyAsync(user => user.Id == id, cancellationToken))
        {
            return NotFound();
        }

        return TypedResults.Ok(new EndedSessionsResponse(await sessions.EndAllAsync(id, cancellationToken)));
    }

    private static async Task<IResult> DetailAsync(
        User user, AppDbContext dbContext, UserSessions sessions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var invitedBy = user.InvitedBy is { } inviterId
            ? await dbContext.Users.AsNoTracking()
                .Where(inviter => inviter.Id == inviterId)
                .Select(inviter => new UserReference(inviter.Id, inviter.Name))
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return TypedResults.Ok(UserDetail.From(user, invitedBy, await sessions.SummarizeAsync(user.Id, cancellationToken), timeProvider.GetUtcNow()));
    }

    public const string NotYetAcceptedMessage = "Este usuário ainda não aceitou o convite.";

    private static IResult NotYetAccepted() =>
        TypedResults.Problem(title: NotYetAcceptedMessage, statusCode: StatusCodes.Status409Conflict);

    private static IResult NotFound() => UserFields.NotFound();

    private static IResult EmailInUse() => UserFields.EmailInUse();
}
