using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;
using Buteco.Api.Users.Sessions;

namespace Buteco.Api.Users.Account;

/// <summary>
/// Os próprios dados e a troca da própria senha, sob <c>/account</c> (design.md da change
/// rotas-conta-usuarios, D1 e D3). Quem chama é o usuário real do token
/// (<see cref="CurrentUser"/>).
///
/// <list type="bullet">
/// <item><b>A senha atual é conferida como no login</b>, e o erro conta no bloqueio do e-mail:
/// um access token roubado (15 min) não testa senhas sem limite. Durante o bloqueio, nada é
/// conferido, e o acerto não zera a contagem, que é do login completo;</item>
/// <item><b>a política vale só para a senha nova</b> (<see cref="PasswordPolicy"/>); a atual
/// pode ser mais curta, como a do bootstrap;</item>
/// <item><b>trocar a senha encerra todas as sessões da pessoa, inclusive a desta
/// requisição</b>: o stamp novo e a revogação das autorizações, numa transação (D2). O painel
/// sai e volta ao <c>/entrar</c> depois do <c>204</c> (#183).</item>
/// </list>
/// </summary>
public static class AccountEndpoints
{
    public const string GroupPath = "/account";

    public sealed record ProfileResponse(Guid Id, string Nome, string Email, DateTimeOffset CriadoEm, DateTimeOffset? UltimoAcesso, bool DuasEtapas);

    public sealed record ChangePasswordRequest(string? SenhaAtual, string? NovaSenha);

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPath);
        group.MapGet("", GetAsync);
        group.MapPost("/password", ChangePasswordAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(HttpContext httpContext, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return CurrentUser.Forbidden();
        }

        return TypedResults.Ok(new ProfileResponse(user.Id, user.Name, user.Email, user.CreatedAt, user.LastAccessAt, user.IsTwoFactorEnabled));
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        UserSessions sessions,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return CurrentUser.Forbidden();
        }

        if (await LoginThrottleResponses.LockedAsync(httpContext, throttle, user, cancellationToken) is { } locked)
        {
            return locked;
        }

        if (string.IsNullOrEmpty(request.SenhaAtual) ||
            hasher.Verify(request.SenhaAtual, user.PasswordHash, user.PasswordHashIsBootstrapLegacy) == PasswordVerificationResult.Failed)
        {
            await throttle.RecordFailureAsync(user.NormalizedEmail, cancellationToken);
            return TypedResults.Problem(title: "Senha atual inválida.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!PasswordPolicy.IsSatisfiedBy(request.NovaSenha))
        {
            return TypedResults.Problem(title: PasswordPolicy.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        user.ChangePassword(hasher.Hash(request.NovaSenha!));
        await dbContext.SaveChangesAsync(cancellationToken);
        await sessions.EndAllAsync(user.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.NoContent();
    }
}
