using System.Text.Json;
using Buteco.Api.Auth;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;
using Buteco.Api.Users.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// As rotas do link, sob <c>/auth</c>, anônimas (design.md da change convites-redefinicao-senha,
/// D6 a D8): consultar e aceitar o convite, consultar e concluir a redefinição.
///
/// <list type="bullet">
/// <item><b>o token chega no corpo de um <c>POST</c></b>, nunca no caminho nem na query: o link o
/// leva no fragmento, e um <c>GET</c> com o token o devolveria aos logs de acesso (D3);</item>
/// <item>as barreiras do login antes de tudo (<see cref="SessionRequestGuards"/>: só JSON e
/// <c>Origin</c> conferida); o aceite e a conclusão no limite global, porque calculam Argon2id; as
/// consultas fora dele (D8);</item>
/// <item><b>link inválido tem uma resposta só</b> (P9): inexistente, vencido, usado, cancelado,
/// substituído, de outro propósito ou de usuário fora do estado esperado → o mesmo <c>404</c>,
/// sem calcular hash de senha;</item>
/// <item><b>uso único</b>: o consumo é um <c>UPDATE</c> só, condicionado ao hash, ao propósito, ao
/// vencimento e ao estado, e zero linhas é o link inválido. Dois usos do mesmo link nunca têm
/// sucesso os dois;</item>
/// <item><b>nenhuma das duas abre sessão</b> (P5): o painel faz o login normal com a senha
/// recém-criada. As duas zeram o bloqueio do e-mail (P8): quem tem o link já controla a conta.</item>
/// </list>
/// </summary>
public static class AccessLinkPublicEndpoints
{
    public const string InvalidLinkMessage =
        "Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador.";

    public sealed record TokenRequest(string? Token);

    public sealed record PasswordRequest(string? Token, string? Senha);

    public sealed record InvitationLookupResponse(string Nome, string Email, string? ConvidadoPor, DateTimeOffset ExpiraEm);

    public sealed record PasswordResetLookupResponse(string Nome, string Email, DateTimeOffset ExpiraEm);

    private sealed record ValidLink(Guid UserId, string Name, string Email, string NormalizedEmail, Guid? InvitedBy, DateTimeOffset ExpiresAt, byte[] Hash);

    public static IEndpointRouteBuilder MapAccessLinkPublicEndpoints(this IEndpointRouteBuilder app)
    {
        Public(app.MapPost("/auth/invitation", LookupInvitationAsync));
        Public(app.MapPost("/auth/invitation/accept", AcceptAsync)).WithMetadata(new LoginRateLimiting.Marker());
        Public(app.MapPost("/auth/password-reset", LookupPasswordResetAsync));
        Public(app.MapPost("/auth/password-reset/complete", CompleteAsync)).WithMetadata(new LoginRateLimiting.Marker());
        return app;
    }

    private static RouteHandlerBuilder Public(RouteHandlerBuilder route) =>
        route.AllowAnonymous().WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.AuthEntryPoint));

    private static async Task<IResult> LookupInvitationAsync(
        HttpContext httpContext, SessionRequestGuards guards, AppDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        var (request, invalidBody) = await ReadAsync<TokenRequest>(httpContext, cancellationToken);
        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (await FindAsync(dbContext, request!.Token, AccessLinkPurpose.Invitation, UserStatus.Invited, timeProvider, cancellationToken) is not { } link)
        {
            return InvalidLink();
        }

        var invitedBy = link.InvitedBy is { } inviterId
            ? await dbContext.Users.Where(user => user.Id == inviterId).Select(user => user.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        return TypedResults.Ok(new InvitationLookupResponse(link.Name, link.Email, invitedBy, link.ExpiresAt));
    }

    private static async Task<IResult> AcceptAsync(
        HttpContext httpContext,
        SessionRequestGuards guards,
        AppDbContext dbContext,
        PasswordHasher hasher,
        LoginThrottleStore throttle,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        var (request, invalidBody) = await ReadAsync<PasswordRequest>(httpContext, cancellationToken);
        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (!PasswordPolicy.IsSatisfiedBy(request!.Senha))
        {
            return PolicyProblem();
        }

        // O link antes do Argon2: o inválido responde sem custo de senha, igual em todos os casos.
        if (await FindAsync(dbContext, request.Token, AccessLinkPurpose.Invitation, UserStatus.Invited, timeProvider, cancellationToken) is not { } link)
        {
            return InvalidLink();
        }

        var passwordHash = hasher.Hash(request.Senha!);
        var now = timeProvider.GetUtcNow();
        var accepted = await dbContext.Users
            .Where(user => user.Id == link.UserId &&
                user.AccessLinkHash == link.Hash &&
                user.AccessLinkPurpose == AccessLinkPurpose.Invitation &&
                user.AccessLinkExpiresAt > now &&
                user.Status == UserStatus.Invited)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.PasswordHash, passwordHash)
                    .SetProperty(user => user.Status, UserStatus.Active)
                    .SetProperty(user => user.AccessLinkHash, (byte[]?)null)
                    .SetProperty(user => user.AccessLinkPurpose, (AccessLinkPurpose?)null)
                    .SetProperty(user => user.AccessLinkExpiresAt, (DateTimeOffset?)null),
                cancellationToken);

        // Outro aceite, um link novo ou o cancelamento entre a busca e a escrita.
        if (accepted == 0)
        {
            return InvalidLink();
        }

        await throttle.ResetAsync(link.NormalizedEmail, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> LookupPasswordResetAsync(
        HttpContext httpContext, SessionRequestGuards guards, AppDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        var (request, invalidBody) = await ReadAsync<TokenRequest>(httpContext, cancellationToken);
        if (invalidBody is not null)
        {
            return invalidBody;
        }

        return await FindAsync(dbContext, request!.Token, AccessLinkPurpose.PasswordReset, UserStatus.Active, timeProvider, cancellationToken) is { } link
            ? TypedResults.Ok(new PasswordResetLookupResponse(link.Name, link.Email, link.ExpiresAt))
            : InvalidLink();
    }

    private static async Task<IResult> CompleteAsync(
        HttpContext httpContext,
        SessionRequestGuards guards,
        AppDbContext dbContext,
        PasswordHasher hasher,
        LoginThrottleStore throttle,
        UserSessions sessions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        var (request, invalidBody) = await ReadAsync<PasswordRequest>(httpContext, cancellationToken);
        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (!PasswordPolicy.IsSatisfiedBy(request!.Senha))
        {
            return PolicyProblem();
        }

        if (await FindAsync(dbContext, request.Token, AccessLinkPurpose.PasswordReset, UserStatus.Active, timeProvider, cancellationToken) is not { } link)
        {
            return InvalidLink();
        }

        var passwordHash = hasher.Hash(request.Senha!);
        var stamp = User.NewSecurityStamp();
        var now = timeProvider.GetUtcNow();

        // A senha, o stamp e as autorizações juntos, como a troca de senha da #219 (D2 dela):
        // concluir a redefinição encerra todas as sessões da pessoa.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var completed = await dbContext.Users
            .Where(user => user.Id == link.UserId &&
                user.AccessLinkHash == link.Hash &&
                user.AccessLinkPurpose == AccessLinkPurpose.PasswordReset &&
                user.AccessLinkExpiresAt > now &&
                user.Status == UserStatus.Active)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.PasswordHash, passwordHash)
                    .SetProperty(user => user.PasswordHashIsBootstrapLegacy, false)
                    .SetProperty(user => user.SecurityStamp, stamp)
                    .SetProperty(user => user.AccessLinkHash, (byte[]?)null)
                    .SetProperty(user => user.AccessLinkPurpose, (AccessLinkPurpose?)null)
                    .SetProperty(user => user.AccessLinkExpiresAt, (DateTimeOffset?)null),
                cancellationToken);

        if (completed == 0)
        {
            return InvalidLink();
        }

        await sessions.EndAllAsync(link.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await throttle.ResetAsync(link.NormalizedEmail, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<ValidLink?> FindAsync(
        AppDbContext dbContext, string? token, AccessLinkPurpose purpose, UserStatus status, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = AccessLinkToken.Hash(token);
        var now = timeProvider.GetUtcNow();
        return await dbContext.Users.AsNoTracking()
            .Where(user => user.AccessLinkHash == hash &&
                user.AccessLinkPurpose == purpose &&
                user.AccessLinkExpiresAt > now &&
                user.Status == status)
            .Select(user => new ValidLink(user.Id, user.Name, user.Email, user.NormalizedEmail, user.InvitedBy, user.AccessLinkExpiresAt!.Value, hash))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<(T? Request, IResult? Invalid)> ReadAsync<T>(HttpContext httpContext, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var request = await httpContext.Request.ReadFromJsonAsync<T>(cancellationToken);
            return request is null
                ? (null, TypedResults.Problem(title: "Corpo do pedido inválido.", statusCode: StatusCodes.Status400BadRequest))
                : (request, null);
        }
        catch (JsonException)
        {
            return (null, TypedResults.Problem(title: "Corpo do pedido inválido.", statusCode: StatusCodes.Status400BadRequest));
        }
    }

    private static IResult InvalidLink() =>
        TypedResults.Problem(title: InvalidLinkMessage, statusCode: StatusCodes.Status404NotFound);

    private static IResult PolicyProblem() =>
        TypedResults.Problem(title: PasswordPolicy.Message, statusCode: StatusCodes.Status400BadRequest);
}
