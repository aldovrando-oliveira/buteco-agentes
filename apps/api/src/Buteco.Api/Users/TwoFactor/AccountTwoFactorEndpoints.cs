using System.Globalization;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// A verificação em 2 etapas da própria pessoa, sob <c>/account/two-factor</c> (design.md da
/// change verificacao-2-etapas-totp, D1, D6 e D8). Quem chama é o usuário real do token
/// (<see cref="CurrentUser"/>).
///
/// <list type="bullet">
/// <item><b>Cadastro em dois tempos:</b> <c>setup</c> pede a senha e grava o segredo pendente
/// por 10 minutos; <c>confirm</c> liga a verificação só com um código válido dele e devolve os
/// 10 códigos de recuperação. Os dois respondem <c>403</c> com o cadastro fechado (D1);</item>
/// <item><b>regeneração e remoção</b> pedem um código válido, do app ou de recuperação;</item>
/// <item><b>toda senha ou código errado conta no bloqueio do e-mail</b>, e durante o bloqueio
/// nada é conferido (D7).</item>
/// </list>
///
/// <para>
/// O segredo sai uma vez, na resposta do <c>setup</c>, e nunca numa leitura: é a exceção
/// declarada ao "write-only" da convenção 3, porque sem ele não há cadastro (D8).
/// </para>
/// </summary>
public static class AccountTwoFactorEndpoints
{
    public const string GroupPath = "/account/two-factor";

    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    public const string EnrollmentClosedMessage = "O cadastro da verificação em 2 etapas ainda não está disponível.";

    public sealed record PasswordRequest(string? Senha);

    public sealed record CodeRequest(string? Codigo);

    public sealed record StatusResponse(bool Ativa, DateTimeOffset? AtivadaEm, int CodigosRestantes, bool CadastroDisponivel);

    public sealed record SetupResponse(string Segredo, string OtpauthUri);

    public sealed record RecoveryCodesResponse(IReadOnlyList<string> CodigosRecuperacao);

    public static IEndpointRouteBuilder MapAccountTwoFactorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPath);
        group.MapGet("", GetAsync);
        group.MapPost("/setup", SetupAsync);
        group.MapPost("/confirm", ConfirmAsync);
        group.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync);
        group.MapPost("/remove", RemoveAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext, AppDbContext dbContext, IOptions<TwoFactorOptions> options, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return CurrentUser.Forbidden();
        }

        var remaining = await dbContext.UserRecoveryCodes.CountAsync(code => code.UserId == user.Id && code.UsedAt == null, cancellationToken);
        return TypedResults.Ok(new StatusResponse(user.IsTwoFactorEnabled, user.TwoFactorEnabledAt, remaining, options.Value.EnrollmentEnabled));
    }

    private static async Task<IResult> SetupAsync(
        PasswordRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        IOptions<TwoFactorOptions> options,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        TwoFactorSecretCipher cipher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!options.Value.EnrollmentEnabled)
        {
            return TypedResults.Problem(title: EnrollmentClosedMessage, statusCode: StatusCodes.Status403Forbidden);
        }

        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return CurrentUser.Forbidden();
        }

        if (await LockedAsync(httpContext, throttle, user, cancellationToken) is { } locked)
        {
            return locked;
        }

        if (user.IsTwoFactorEnabled)
        {
            return TypedResults.Problem(title: "A verificação em 2 etapas já está ligada.", statusCode: StatusCodes.Status409Conflict);
        }

        if (string.IsNullOrEmpty(request.Senha) ||
            hasher.Verify(request.Senha, user.PasswordHash, user.PasswordHashIsBootstrapLegacy) == PasswordVerificationResult.Failed)
        {
            await throttle.RecordFailureAsync(user.NormalizedEmail, cancellationToken);
            return TypedResults.Problem(title: "Senha inválida.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var secret = Totp.NewSecret();
        user.StartTwoFactorEnrollment(cipher.Protect(user.Id, secret), timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new SetupResponse(Base32.Encode(secret), OtpAuthUri.Build(user.Email, secret)));
    }

    private static async Task<IResult> ConfirmAsync(
        CodeRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        IOptions<TwoFactorOptions> options,
        LoginThrottleStore throttle,
        TwoFactorVerifier verifier,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!options.Value.EnrollmentEnabled)
        {
            return TypedResults.Problem(title: EnrollmentClosedMessage, statusCode: StatusCodes.Status403Forbidden);
        }

        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return CurrentUser.Forbidden();
        }

        if (await LockedAsync(httpContext, throttle, user, cancellationToken) is { } locked)
        {
            return locked;
        }

        // O pendente vale 10 minutos, pela regra do tempo e não por limpeza.
        var now = timeProvider.GetUtcNow();
        if (user.TwoFactorPendingAt is not { } startedAt || now - startedAt > PendingLifetime)
        {
            return TypedResults.Problem(title: "Não há cadastro em curso. Comece de novo.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (verifier.MatchPending(user, request.Codigo) is not { } step)
        {
            await throttle.RecordFailureAsync(user.NormalizedEmail, cancellationToken);
            return TypedResults.Problem(title: SessionEndpoints.InvalidCodeMessage, statusCode: StatusCodes.Status401Unauthorized);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        user.EnableTwoFactor(now, step);
        await dbContext.SaveChangesAsync(cancellationToken);
        var codes = await verifier.ReplaceRecoveryCodesAsync(user.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(new RecoveryCodesResponse(codes));
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

    private static async Task<IResult> RemoveAsync(
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
        user!.RemoveTwoFactor();
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.UserRecoveryCodes.Where(code => code.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.NoContent();
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

        if (await LockedAsync(httpContext, throttle, user, cancellationToken) is { } locked)
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

    private static async Task<IResult?> LockedAsync(HttpContext httpContext, LoginThrottleStore throttle, User user, CancellationToken cancellationToken)
    {
        if (await throttle.GetLockRemainingAsync(user.NormalizedEmail, cancellationToken) is not { } remaining)
        {
            return null;
        }

        httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(remaining.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return TypedResults.Problem(title: SessionEndpoints.LockedMessage, statusCode: StatusCodes.Status423Locked);
    }
}
