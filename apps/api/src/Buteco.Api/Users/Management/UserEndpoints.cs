using System.Net.Mail;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Sessions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.Users.Management;

/// <summary>
/// Usuários, sob <c>/users</c> (design.md da change rotas-conta-usuarios, D1 e D5 a D8). Na M1,
/// sem papéis, todo usuário real chama todas (<see cref="CurrentUser"/>), e o HMAC de operador
/// recebe <c>403</c>. A #184 as restringe ao Admin. A redefinição das 2 etapas, no mesmo
/// prefixo, é da <c>UserTwoFactorResetEndpoint</c>.
///
/// <list type="bullet">
/// <item><b>Editar</b> não troca o stamp nem pede senha: o e-mail é o identificador do login,
/// e não um segredo, e na M1 nada é enviado ao endereço cadastrado (D5, com o gatilho para
/// voltar registrado na #181). O <c>409</c> vale também na corrida, pelo índice único;</item>
/// <item><b>desativar</b> troca o stamp e revoga as sessões, sob o lock
/// <see cref="UserAdministration.AdvisoryLockKey"/>, relendo quem chama dentro dele (D7);</item>
/// <item><b>desativar e reativar são idempotentes</b>, como em <c>McpServers</c>: o estado já
/// pedido responde <c>200</c> sem gravar nada.</item>
/// </list>
/// </summary>
public static class UserEndpoints
{
    public const string GroupPath = "/users";

    public const int MaxNameLength = 200;

    public const int MaxEmailLength = 254;

    private const string NormalizedEmailIndexName = "IX_users_normalized_email";

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

    private static async Task<IResult> ListAsync(HttpContext httpContext, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        var users = await dbContext.Users.AsNoTracking()
            .OrderBy(user => user.Name)
            .ThenBy(user => user.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(users.Select(UserSummary.From).ToList());
    }

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, UserSessions sessions, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        return await dbContext.Users.FindAsync([id], cancellationToken) is { } user
            ? await DetailAsync(user, sessions, cancellationToken)
            : NotFound();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        UserSessions sessions,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        var name = request.Nome?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length is 0 or > MaxNameLength)
        {
            errors["nome"] = [$"O nome precisa ter de 1 a {MaxNameLength} caracteres."];
        }

        if (email.Length > MaxEmailLength || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
        {
            errors["email"] = ["Informe um e-mail válido."];
        }

        if (errors.Count > 0)
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
        catch (DbUpdateException exception) when (IsNormalizedEmailUniqueViolation(exception))
        {
            // Outra edição gravou o mesmo e-mail entre a conferência e esta escrita.
            return EmailInUse();
        }

        return await DetailAsync(user, sessions, cancellationToken);
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

        if (target.IsActive)
        {
            target.Deactivate(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            await sessions.EndAllAsync(target.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(target, sessions, cancellationToken);
    }

    private static async Task<IResult> ActivateAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, UserSessions sessions, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (await dbContext.Users.FindAsync([id], cancellationToken) is not { } user)
        {
            return NotFound();
        }

        if (!user.IsActive)
        {
            user.Activate();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await DetailAsync(user, sessions, cancellationToken);
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

    private static async Task<IResult> DetailAsync(User user, UserSessions sessions, CancellationToken cancellationToken) =>
        TypedResults.Ok(UserDetail.From(user, await sessions.SummarizeAsync(user.Id, cancellationToken)));

    private static IResult NotFound() =>
        TypedResults.Problem(title: "Usuário não encontrado.", statusCode: StatusCodes.Status404NotFound);

    private static IResult EmailInUse() =>
        TypedResults.Problem(title: "Já existe um usuário com este e-mail.", statusCode: StatusCodes.Status409Conflict);

    private static bool IsNormalizedEmailUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        postgres.ConstraintName == NormalizedEmailIndexName;
}
