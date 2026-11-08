using System.Globalization;
using System.Text.Json;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Flow;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Passwords;
using Buteco.Api.Users.TwoFactor;
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
/// <item>sucesso sem a verificação em 2 etapas → zera a contagem, refaz o hash se preciso e
/// grava o cookie pendente, <c>200 { "segundoPasso": "cadastro" }</c>: a verificação é
/// obrigatória, e a sessão se completa pelo cadastro no login, sem <c>204</c> nesta rota
/// (design.md da change verificacao-2-etapas-obrigatoria, D1);</item>
/// <item>sucesso com a verificação ligada → refaz o hash se preciso e grava o cookie da sessão
/// pendente, <c>200 { "segundoPasso": "totp" }</c>, <b>sem zerar a contagem e sem último
/// acesso</b>: se zerasse, quem tem a senha testaria códigos sem fim (design.md da change
/// verificacao-2-etapas-totp, D2 e D7).</item>
/// </list>
///
/// <para>
/// <c>POST /auth/session/second-factor</c> completa a sessão pendente com o código do app ou
/// um de recuperação, com os mesmos guardas e o mesmo limite global, e as falhas no mesmo
/// bloqueio do e-mail.
/// </para>
/// </summary>
public static class SessionEndpoints
{
    public const string InvalidCredentialsMessage = "E-mail ou senha inválidos.";
    public const string DeactivatedMessage = "Acesso desativado. Fale com o administrador.";
    public const string LockedMessage = "Acesso pausado por tentativas seguidas. Tente de novo mais tarde.";
    public const string ExpiredMessage = "A entrada expirou. Informe e-mail e senha de novo.";
    public const string InvalidCodeMessage = "Código inválido.";
    public const string UnreadableSecretMessage =
        "Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador.";

    public const string SecondFactorPath = "/auth/session/second-factor";

    /// <summary>O valor de <c>segundoPasso</c> de quem tem a verificação: o código.</summary>
    public const string CodeStep = "totp";

    /// <summary>O valor de <c>segundoPasso</c> de quem não tem a verificação: o cadastro no login.</summary>
    public const string EnrollmentStep = "cadastro";

    public sealed record SessionRequest(string? Email, string? Senha);

    public sealed record SecondFactorRequest(string? Codigo);

    public sealed record SecondStepResponse(string SegundoPasso);

    public const string EnrollmentSetupPath = "/auth/session/enrollment/setup";

    public const string EnrollmentConfirmPath = "/auth/session/enrollment/confirm";

    public sealed record EnrollmentSetupResponse(string Segredo, string OtpauthUri);

    public sealed record EnrollmentConfirmResponse(IReadOnlyList<string> CodigosRecuperacao);

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/session", CreateAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.AuthEntryPoint))
            .WithMetadata(new LoginRateLimiting.Marker());

        app.MapPost(SecondFactorPath, SecondFactorAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.AuthEntryPoint))
            .WithMetadata(new LoginRateLimiting.Marker());

        app.MapPost(EnrollmentSetupPath, EnrollmentSetupAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.AuthEntryPoint))
            .WithMetadata(new LoginRateLimiting.Marker());

        app.MapPost(EnrollmentConfirmPath, EnrollmentConfirmAsync)
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

        if (verification == PasswordVerificationResult.SucceededNeedsRehash)
        {
            user.ReplacePasswordHash(hasher.Hash(request.Senha));
        }

        if (user.IsTwoFactorEnabled)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await IssuerSession.SignInAsync(httpContext, user, twoFactorDone: false);
            return TypedResults.Ok(new SecondStepResponse(CodeStep));
        }

        // Sem a verificação, o cadastro no login (design.md da change
        // verificacao-2-etapas-obrigatoria, D1 e D8): a contagem zera, porque no cadastro não há
        // segredo a adivinhar, e o último acesso espera a sessão completa, no confirm.
        await throttle.ResetAsync(normalizedEmail, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await IssuerSession.SignInAsync(httpContext, user, twoFactorDone: false);
        return TypedResults.Ok(new SecondStepResponse(EnrollmentStep));
    }

    private static async Task<IResult> SecondFactorAsync(
        HttpContext httpContext,
        SessionRequestGuards guards,
        LoginThrottleStore throttle,
        TwoFactorVerifier verifier,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        SecondFactorRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<SecondFactorRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.Problem(title: "Corpo do código inválido.", statusCode: StatusCodes.Status400BadRequest);
        }

        // A sessão pendente: cookie válido de usuário ativo, com o stamp gravado e a verificação
        // ainda ligada. Sem ela não há e-mail a quem atribuir falha.
        var session = await httpContext.AuthenticateAsync(IssuerSession.SchemeName);
        var user = session.Succeeded && Guid.TryParse(session.Principal.FindFirst(IssuerSession.UserIdClaim)?.Value, out var userId)
            ? await dbContext.Users.FindAsync([userId], cancellationToken)
            : null;
        if (user is not { IsActive: true, IsTwoFactorEnabled: true } ||
            user.SecurityStamp != session.Principal!.FindFirst(IssuerSession.SecurityStampClaim)?.Value)
        {
            return TypedResults.Problem(title: ExpiredMessage, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (await throttle.GetLockRemainingAsync(user.NormalizedEmail, cancellationToken) is { } remaining)
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(remaining.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return TypedResults.Problem(title: LockedMessage, statusCode: StatusCodes.Status423Locked);
        }

        switch (await verifier.VerifyAsync(user, request?.Codigo, cancellationToken))
        {
            case TwoFactorCheck.SecretUnreadable:
                return TypedResults.Problem(title: UnreadableSecretMessage, statusCode: StatusCodes.Status500InternalServerError);
            case TwoFactorCheck.Rejected:
                await throttle.RecordFailureAsync(user.NormalizedEmail, cancellationToken);
                return TypedResults.Problem(title: InvalidCodeMessage, statusCode: StatusCodes.Status401Unauthorized);
        }

        await throttle.ResetAsync(user.NormalizedEmail, cancellationToken);
        user.RecordAccess(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        await IssuerSession.SignInAsync(httpContext, user, twoFactorDone: true);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// <c>POST /auth/session/enrollment/setup</c> (design.md da change
    /// verificacao-2-etapas-obrigatoria, D1 e D2): um segredo pendente novo para o usuário do
    /// cookie pendente, sem pedir a senha de novo, porque o cookie acabou de prová-la. O primeiro
    /// <c>setup</c> do cookie abre o prazo de 10 minutos; os seguintes trocam o segredo e mantêm o
    /// fim do prazo. O cookie é regravado para vencer nesse instante.
    /// </summary>
    private static async Task<IResult> EnrollmentSetupAsync(
        HttpContext httpContext,
        SessionRequestGuards guards,
        LoginThrottleStore throttle,
        TwoFactorEnrollment enrollment,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        var (user, session) = await EnrollingUserAsync(httpContext, dbContext, cancellationToken);
        if (user is null)
        {
            return Expired();
        }

        if (await LoginThrottleResponses.LockedAsync(httpContext, throttle, user, cancellationToken) is { } locked)
        {
            return locked;
        }

        var started = await enrollment.StartAsync(user, IssuerSession.EnrollmentDeadline(session!), cancellationToken);
        await IssuerSession.SignInAsync(httpContext, user, twoFactorDone: false, enrollmentDeadline: started.Deadline);
        return TypedResults.Ok(new EnrollmentSetupResponse(started.Segredo, started.OtpauthUri));
    }

    /// <summary>
    /// <c>POST /auth/session/enrollment/confirm</c>: liga a verificação com um código válido do
    /// segredo pendente e só então completa a sessão. Sem o prazo gravado por um <c>setup</c>
    /// deste cookie, ou com o pendente vencido, a entrada expirou.
    /// </summary>
    private static async Task<IResult> EnrollmentConfirmAsync(
        HttpContext httpContext,
        SessionRequestGuards guards,
        LoginThrottleStore throttle,
        TwoFactorEnrollment enrollment,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        SecondFactorRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<SecondFactorRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.Problem(title: "Corpo do código inválido.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (user, session) = await EnrollingUserAsync(httpContext, dbContext, cancellationToken);
        if (user is null)
        {
            return Expired();
        }

        if (await LoginThrottleResponses.LockedAsync(httpContext, throttle, user, cancellationToken) is { } locked)
        {
            return locked;
        }

        if (IssuerSession.EnrollmentDeadline(session!) is null || !enrollment.IsPendingLive(user))
        {
            return Expired();
        }

        if (enrollment.Match(user, request?.Codigo) is not { } step)
        {
            await throttle.RecordFailureAsync(user.NormalizedEmail, cancellationToken);
            return TypedResults.Problem(title: InvalidCodeMessage, statusCode: StatusCodes.Status401Unauthorized);
        }

        var codes = await enrollment.CompleteAsync(user, step, cancellationToken);
        await throttle.ResetAsync(user.NormalizedEmail, cancellationToken);
        user.RecordAccess(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        await IssuerSession.SignInAsync(httpContext, user, twoFactorDone: true);
        return TypedResults.Ok(new EnrollmentConfirmResponse(codes));
    }

    /// <summary>
    /// O usuário do cookie pendente que pode cadastrar: cookie válido, usuário ativo, stamp igual
    /// ao gravado e <b>sem</b> a verificação ligada. As rotas atuam só sobre ele, e nunca sobre um
    /// usuário ou e-mail do corpo (D1).
    /// </summary>
    private static async Task<(User? User, System.Security.Claims.ClaimsPrincipal? Session)> EnrollingUserAsync(
        HttpContext httpContext, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var session = await httpContext.AuthenticateAsync(IssuerSession.SchemeName);
        var user = session.Succeeded && Guid.TryParse(session.Principal.FindFirst(IssuerSession.UserIdClaim)?.Value, out var userId)
            ? await dbContext.Users.FindAsync([userId], cancellationToken)
            : null;
        return user is { IsActive: true, IsTwoFactorEnabled: false } &&
            user.SecurityStamp == session.Principal!.FindFirst(IssuerSession.SecurityStampClaim)?.Value
            ? (user, session.Principal)
            : (null, null);
    }

    private static IResult Expired() =>
        TypedResults.Problem(title: ExpiredMessage, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult InvalidCredentials() =>
        TypedResults.Problem(title: InvalidCredentialsMessage, statusCode: StatusCodes.Status401Unauthorized);
}
