using Buteco.Api.Identity;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Management;
using Buteco.Api.Users.Passwords;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// <c>POST /users/{id}/password-reset</c> (design.md da change convites-redefinicao-senha, D1 e
/// D7): o link de redefinição, de 24 h, devolvido uma vez para o admin copiar. Não toca a senha, o
/// stamp nem as sessões do alvo: a senha atual vale até a nova ser criada.
///
/// <list type="bullet">
/// <item><b>pede a senha de quem chama</b> (P10, <see cref="CallerPassword"/>): o link é o caminho
/// de um access token roubado para a senha de outra pessoa;</item>
/// <item><b>a si mesmo → <c>403</c></b>, antes de conferir a senha (P7): o caminho é Minha conta,
/// que pede a senha atual;</item>
/// <item>só no ativo; o <c>UPDATE</c> é condicionado ao estado ativo, para uma desativação no meio
/// não deixar um link num desativado, que a reativação traria de volta.</item>
/// </list>
/// </summary>
public static class PasswordResetEndpoints
{
    public const string SelfMessage = "Use Minha conta para trocar a própria senha.";
    public const string DeactivatedMessage = "Usuário desativado.";

    public static IEndpointRouteBuilder MapPasswordResetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup(UserEndpoints.GroupPath).MapPost("/{id:guid}/password-reset", CreateAsync);
        return app;
    }

    private static async Task<IResult> CreateAsync(
        Guid id,
        InvitationEndpoints.CallerPasswordRequest? request,
        HttpContext httpContext,
        AppDbContext dbContext,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        IdentityIssuerState issuer,
        IOptions<IdentityIssuerOptions> options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        if (await LoginThrottleResponses.LockedAsync(httpContext, throttle, caller, cancellationToken) is { } locked)
        {
            return locked;
        }

        if (caller.Id == id)
        {
            return TypedResults.Problem(title: SelfMessage, statusCode: StatusCodes.Status403Forbidden);
        }

        var status = await dbContext.Users.Where(user => user.Id == id).Select(user => (UserStatus?)user.Status).SingleOrDefaultAsync(cancellationToken);
        switch (status)
        {
            case null:
                return UserFields.NotFound();
            case UserStatus.Invited:
                return TypedResults.Problem(title: UserEndpoints.NotYetAcceptedMessage, statusCode: StatusCodes.Status409Conflict);
            case UserStatus.Deactivated:
                return Deactivated();
        }

        if (await CallerPassword.RejectAsync(caller, request?.Senha, hasher, throttle, cancellationToken) is { } rejected)
        {
            return rejected;
        }

        var token = AccessLinkToken.New();
        var hash = AccessLinkToken.Hash(token);
        var expiresAt = timeProvider.GetUtcNow() + AccessLinkToken.PasswordResetLifetime;
        var issued = await dbContext.Users
            .Where(user => user.Id == id && user.Status == UserStatus.Active)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.AccessLinkHash, hash)
                    .SetProperty(user => user.AccessLinkPurpose, AccessLinkPurpose.PasswordReset)
                    .SetProperty(user => user.AccessLinkExpiresAt, expiresAt),
                cancellationToken);

        // Desativado entre a leitura e a escrita.
        return issued == 1
            ? TypedResults.Ok(new InvitationEndpoints.LinkResponse(
                AccessLinkUrls.For(AccessLinkPurpose.PasswordReset, PanelBaseUrl.Resolve(options.Value, issuer), token), expiresAt))
            : Deactivated();
    }

    private static IResult Deactivated() =>
        TypedResults.Problem(title: DeactivatedMessage, statusCode: StatusCodes.Status409Conflict);
}
