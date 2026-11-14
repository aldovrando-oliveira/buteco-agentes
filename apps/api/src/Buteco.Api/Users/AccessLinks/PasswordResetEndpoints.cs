using Buteco.Api.Identity;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Management;
using Buteco.Api.Users.Passwords;
using Buteco.Api.Users.TwoFactor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// <c>POST /users/{id}/password-reset</c> (design.md da change convites-redefinicao-senha, D1 e
/// D7): o link de redefinição, de 24 h, devolvido uma vez para o admin copiar. Não toca a senha, o
/// stamp nem as sessões do alvo: a senha atual vale até a nova ser criada.
///
/// <list type="bullet">
/// <item><b>só o Admin</b>, pelo papel gravado (<see cref="CurrentUser.FindAdminAsync"/>; design.md da
/// change papeis-no-token, D3), e o papel do alvo não restringe: um Admin gera a de outro Admin (Q6;
/// recusar seria contornável por rebaixar, gerar e promover);</item>
/// <item><b>pede a senha de quem chama</b> (P10, <see cref="CallerPassword"/>): o link é o caminho
/// de um access token roubado para a senha de outra pessoa;</item>
/// <item><b>a si mesmo → <c>403</c></b>, antes de conferir a senha (P7): o caminho é Minha conta,
/// que pede a senha atual;</item>
/// <item>só no ativo; o <c>UPDATE</c> é condicionado ao estado ativo, para uma desativação no meio
/// não deixar um link num desativado, que a reativação traria de volta.</item>
/// <item>a escrita é o <see cref="PasswordResetLink"/>, a mesma do comando de emergência
/// <c>password-reset-link</c> (design.md da change comando-password-reset-link, D1).</item>
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
        TwoFactorVerifier verifier,
        IdentityIssuerState issuer,
        IOptions<IdentityIssuerOptions> options,
        PasswordResetLink link,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
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

        if (await CallerPassword.RejectAsync(caller, request?.Senha, request?.Codigo, hasher, verifier, throttle, cancellationToken) is { } rejected)
        {
            return rejected;
        }

        // A base antes da escrita (design.md da change comando-password-reset-link, D1): aqui o boot
        // já exige o issuer, e a ordem não muda nada visível; é a mesma do comando, onde muda.
        var panelBase = PanelBaseUrl.Resolve(options.Value, issuer);

        // Desativado entre a leitura e a escrita: null.
        return await link.IssueAsync(id, panelBase, cancellationToken) is { } issued
            ? TypedResults.Ok(new InvitationEndpoints.LinkResponse(issued.Link, issued.ExpiresAt))
            : Deactivated();
    }

    private static IResult Deactivated() =>
        TypedResults.Problem(title: DeactivatedMessage, statusCode: StatusCodes.Status409Conflict);
}
