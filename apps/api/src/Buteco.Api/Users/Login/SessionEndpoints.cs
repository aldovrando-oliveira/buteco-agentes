using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Flow;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Passwords;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.Login;

/// <summary>
/// <c>POST /auth/session</c> (design.md da change usuarios-locais-login-emissor, D1 e D4):
/// confere e-mail e senha e grava o cookie de sessão do emissor, que o authorize lê.
///
/// <list type="number">
/// <item>só JSON e <c>Origin</c> conferida (<see cref="SessionRequestGuards"/>), antes de tudo;</item>
/// <item>bloqueio do e-mail → <c>423</c> com <c>Retry-After</c>, sem verificar a senha;</item>
/// <item>e-mail sem conta → verificação contra o hash fictício, falha contada, <c>401</c>;</item>
/// <item>senha errada → falha contada, <c>401</c>, com a mesma mensagem;</item>
/// <item>desativado com a senha certa → <c>403</c>, sem contar falha e sem zerar;</item>
/// <item>sucesso → zera a contagem, refaz o hash se preciso, último acesso, cookie, <c>204</c>.</item>
/// </list>
/// </summary>
public static class SessionEndpoints
{
    public const string InvalidCredentialsMessage = "E-mail ou senha inválidos.";
    public const string DeactivatedMessage = "Acesso desativado. Fale com o administrador.";
    public const string LockedMessage = "Acesso pausado por tentativas seguidas. Tente de novo mais tarde.";

    public sealed record SessionRequest(string? Email, string? Senha);

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/session", CreateAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.AuthEntryPoint))
            .WithMetadata(new LoginRateLimiting.Marker());

        return app;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext httpContext,
        SessionRequestGuards guards,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        SessionRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<SessionRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.Problem(title: "Corpo do login inválido.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.Email) || string.IsNullOrEmpty(request.Senha))
        {
            return TypedResults.Problem(title: "Informe e-mail e senha.", statusCode: StatusCodes.Status400BadRequest);
        }

        var normalizedEmail = EmailNormalization.Normalize(request.Email);

        if (await throttle.GetLockRemainingAsync(normalizedEmail, cancellationToken) is { } remaining)
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(remaining.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return TypedResults.Problem(title: LockedMessage, statusCode: StatusCodes.Status423Locked);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null)
        {
            hasher.VerifyAgainstDummy(request.Senha);
            await throttle.RecordFailureAsync(normalizedEmail, cancellationToken);
            return InvalidCredentials();
        }

        var verification = hasher.Verify(request.Senha, user.PasswordHash, user.PasswordHashIsBootstrapLegacy);
        if (verification == PasswordVerificationResult.Failed)
        {
            await throttle.RecordFailureAsync(normalizedEmail, cancellationToken);
            return InvalidCredentials();
        }

        // Só quem já provou a senha vê a mensagem distinta (decisão do dono, 28/10/2026).
        if (!user.IsActive)
        {
            return TypedResults.Problem(title: DeactivatedMessage, statusCode: StatusCodes.Status403Forbidden);
        }

        await throttle.ResetAsync(normalizedEmail, cancellationToken);
        if (verification == PasswordVerificationResult.SucceededNeedsRehash)
        {
            user.ReplacePasswordHash(hasher.Hash(request.Senha));
        }

        user.RecordAccess(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        var identity = new ClaimsIdentity(
            [
                new Claim(IssuerSession.UserIdClaim, user.Id.ToString()),
                new Claim(IssuerSession.SecurityStampClaim, user.SecurityStamp),
            ],
            IssuerSession.SchemeName,
            IssuerSession.UserIdClaim,
            roleType: null);

        await httpContext.SignInAsync(
            IssuerSession.SchemeName,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = false });

        return TypedResults.NoContent();
    }

    private static IResult InvalidCredentials() =>
        TypedResults.Problem(title: InvalidCredentialsMessage, statusCode: StatusCodes.Status401Unauthorized);
}
