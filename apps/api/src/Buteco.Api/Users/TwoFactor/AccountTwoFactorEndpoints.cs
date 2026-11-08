using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Login;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// A verificação em 2 etapas da própria pessoa, sob <c>/account/two-factor</c> (design.md da
/// change verificacao-2-etapas-totp, D6 e D8). Quem chama é o usuário real do token
/// (<see cref="CurrentUser"/>).
///
/// <list type="bullet">
/// <item><b>a situação</b> (<c>GET</c>): ligada, desde quando e quantos códigos de recuperação restam;</item>
/// <item><b>a regeneração dos códigos</b> pede um código válido, do app ou de recuperação, e toda
/// falha conta no bloqueio do e-mail; durante o bloqueio nada é conferido (D7).</item>
/// </list>
///
/// <para>
/// <b>Sem cadastro e sem remoção aqui</b> (design.md da change verificacao-2-etapas-obrigatoria,
/// D5; decisão do dono, Q5): a verificação é obrigatória, e o único caminho de cadastro é o do
/// login (<c>/auth/session/enrollment/…</c>). Quem troca de celular pede a redefinição a outro
/// usuário, ou usa o comando de emergência no servidor.
/// </para>
/// </summary>
public static class AccountTwoFactorEndpoints
{
    public const string GroupPath = "/account/two-factor";

    public sealed record CodeRequest(string? Codigo);

    public sealed record StatusResponse(bool Ativa, DateTimeOffset? AtivadaEm, int CodigosRestantes);

    public sealed record RecoveryCodesResponse(IReadOnlyList<string> CodigosRecuperacao);

    public static IEndpointRouteBuilder MapAccountTwoFactorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPath);
        group.MapGet("", GetAsync);
        group.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(HttpContext httpContext, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return CurrentUser.Forbidden();
        }

        var remaining = await dbContext.UserRecoveryCodes.CountAsync(code => code.UserId == user.Id && code.UsedAt == null, cancellationToken);
        return TypedResults.Ok(new StatusResponse(user.IsTwoFactorEnabled, user.TwoFactorEnabledAt, remaining));
    }

    private static async Task<IResult> RegenerateRecoveryCodesAsync(
        CodeRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        LoginThrottleStore throttle,
        TwoFactorVerifier verifier,
        CancellationToken cancellationToken)
    {
        var (user, refused) = await VerifiedUserAsync(request, httpContext, dbContext, throttle, verifier, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var codes = await verifier.ReplaceRecoveryCodesAsync(user!.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    /// <summary>O usuário com a verificação ligada que provou um código válido, ou a recusa.</summary>
    private static async Task<(User? User, IResult? Refused)> VerifiedUserAsync(
        CodeRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        LoginThrottleStore throttle,
        TwoFactorVerifier verifier,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return (null, CurrentUser.Forbidden());
        }

        if (!user.IsTwoFactorEnabled)
        {
            return (null, TypedResults.Problem(title: "A verificação em 2 etapas não está ligada.", statusCode: StatusCodes.Status409Conflict));
        }

        if (await LoginThrottleResponses.LockedAsync(httpContext, throttle, user, cancellationToken) is { } locked)
        {
            return (null, locked);
        }

        switch (await verifier.VerifyAsync(user, request.Codigo, cancellationToken))
        {
            case TwoFactorCheck.SecretUnreadable:
                return (null, TypedResults.Problem(title: SessionEndpoints.UnreadableSecretMessage, statusCode: StatusCodes.Status500InternalServerError));
            case TwoFactorCheck.Rejected:
                await throttle.RecordFailureAsync(user.NormalizedEmail, cancellationToken);
                return (null, TypedResults.Problem(title: SessionEndpoints.InvalidCodeMessage, statusCode: StatusCodes.Status401Unauthorized));
            default:
                return (user, null);
        }
    }
}
