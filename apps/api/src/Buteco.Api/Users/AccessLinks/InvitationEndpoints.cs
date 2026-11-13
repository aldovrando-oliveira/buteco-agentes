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
/// O convite, sob <c>/users</c> (design.md da change convites-redefinicao-senha, D1, D2, D5 e
/// D10). Só o Admin chama, pelo papel gravado (<see cref="CurrentUser.FindAdminAsync"/>; design.md
/// da change papeis-no-token, D3).
///
/// <list type="bullet">
/// <item><b>Convidar</b> cria o usuário no estado convidado, sem senha, e devolve o link uma vez.
/// A unicidade do e-mail com convites pendentes é a do índice de <c>users</c>, inclusive na
/// corrida;</item>
/// <item><b>criar e gerar novo link pedem a senha de quem chama</b> (P10, <see cref="CallerPassword"/>):
/// são o caminho de um access token roubado para um usuário novo. A ordem é a do <c>setup</c> das
/// 2 etapas: quem chama, bloqueio, corpo, alvo, senha, grava;</item>
/// <item><b>gerar novo link</b> sobrescreve o hash num <c>UPDATE</c> condicionado ao estado
/// convidado: um aceite no meio faz a renovação responder <c>409</c>, sem gravar link num
/// ativo;</item>
/// <item><b>cancelar</b> apaga a linha, num <c>DELETE</c> condicionado ao estado convidado: o
/// convidado nunca foi usuário, e o e-mail volta a valer para um convite novo. Não pede senha,
/// porque só tira acesso.</item>
/// </list>
/// </summary>
public static class InvitationEndpoints
{
    public const string NotInvitedMessage = "Este usuário não tem convite pendente.";
    public const string AlreadyAcceptedMessage = "O convite já foi aceito.";

    // Papel obrigatório, em admin, operador ou leitor (design.md da change papeis-no-token, D5, Q9):
    // sem padrão, que seria uma decisão de acesso tomada em silêncio. O aceite mantém o papel.
    public sealed record InvitationRequest(string? Nome, string? Email, string? Papel, string? Senha, string? Codigo = null);

    public sealed record CallerPasswordRequest(string? Senha, string? Codigo = null);

    public sealed record InvitationCreatedResponse(UserSummary Usuario, string Link, DateTimeOffset ExpiraEm);

    public sealed record LinkResponse(string Link, DateTimeOffset ExpiraEm);

    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(UserEndpoints.GroupPath);
        group.MapPost("/invitations", CreateAsync);
        group.MapPost("/{id:guid}/invitation/renew", RenewAsync);
        group.MapPost("/{id:guid}/invitation/cancel", CancelAsync);
        return app;
    }

    private static async Task<IResult> CreateAsync(
        InvitationRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        TwoFactorVerifier verifier,
        IdentityIssuerState issuer,
        IOptions<IdentityIssuerOptions> options,
        TimeProvider timeProvider,
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

        if (UserFields.Validate(request.Nome, request.Email, out var name, out var email) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Antes da senha: o corpo inválido não conta no bloqueio de quem chama.
        if (UserRoleText.TryReadWire(request.Papel) is not { } role)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["papel"] = ["O papel precisa ser admin, operador ou leitor."],
            });
        }

        var normalized = EmailNormalization.Normalize(email);
        if (await dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalized, cancellationToken))
        {
            return UserFields.EmailInUse();
        }

        if (await CallerPassword.RejectAsync(caller, request.Senha, request.Codigo, hasher, verifier, throttle, cancellationToken) is { } rejected)
        {
            return rejected;
        }

        var now = timeProvider.GetUtcNow();
        var token = AccessLinkToken.New();
        var expiresAt = now + AccessLinkToken.InvitationLifetime;
        var invited = User.CreateInvitation(email, name, caller.Id, now, role);
        invited.IssueAccessLink(AccessLinkPurpose.Invitation, AccessLinkToken.Hash(token), expiresAt);
        dbContext.Users.Add(invited);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (UserFields.IsNormalizedEmailUniqueViolation(exception))
        {
            // Outro convite ou edição gravou o mesmo e-mail entre a conferência e esta escrita.
            return UserFields.EmailInUse();
        }

        var link = AccessLinkUrls.For(AccessLinkPurpose.Invitation, PanelBaseUrl.Resolve(options.Value, issuer), token);
        return TypedResults.Created(
            $"{UserEndpoints.GroupPath}/{invited.Id}",
            new InvitationCreatedResponse(UserSummary.From(invited, now), link, expiresAt));
    }

    private static async Task<IResult> RenewAsync(
        Guid id,
        CallerPasswordRequest? request,
        HttpContext httpContext,
        AppDbContext dbContext,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        TwoFactorVerifier verifier,
        IdentityIssuerState issuer,
        IOptions<IdentityIssuerOptions> options,
        TimeProvider timeProvider,
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

        var status = await dbContext.Users.Where(user => user.Id == id).Select(user => (UserStatus?)user.Status).SingleOrDefaultAsync(cancellationToken);
        if (status is null)
        {
            return UserFields.NotFound();
        }

        if (status != UserStatus.Invited)
        {
            return NotInvited();
        }

        if (await CallerPassword.RejectAsync(caller, request?.Senha, request?.Codigo, hasher, verifier, throttle, cancellationToken) is { } rejected)
        {
            return rejected;
        }

        var token = AccessLinkToken.New();
        var hash = AccessLinkToken.Hash(token);
        var expiresAt = timeProvider.GetUtcNow() + AccessLinkToken.InvitationLifetime;
        var renewed = await dbContext.Users
            .Where(user => user.Id == id && user.Status == UserStatus.Invited)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.AccessLinkHash, hash)
                    .SetProperty(user => user.AccessLinkPurpose, AccessLinkPurpose.Invitation)
                    .SetProperty(user => user.AccessLinkExpiresAt, expiresAt),
                cancellationToken);

        // Aceito ou cancelado entre a leitura e a escrita.
        return renewed == 1
            ? TypedResults.Ok(new LinkResponse(AccessLinkUrls.For(AccessLinkPurpose.Invitation, PanelBaseUrl.Resolve(options.Value, issuer), token), expiresAt))
            : NotInvited();
    }

    private static async Task<IResult> CancelAsync(Guid id, HttpContext httpContext, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        // Condicionado ao estado na mesma instrução: um aceite confirmado antes faz apagar 0 linhas,
        // e nunca um usuário ativo (tasks.md, 2.4).
        var deleted = await dbContext.Users
            .Where(user => user.Id == id && user.Status == UserStatus.Invited)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 1)
        {
            return TypedResults.NoContent();
        }

        return await dbContext.Users.AnyAsync(user => user.Id == id, cancellationToken)
            ? TypedResults.Problem(title: AlreadyAcceptedMessage, statusCode: StatusCodes.Status409Conflict)
            : UserFields.NotFound();
    }

    private static IResult NotInvited() =>
        TypedResults.Problem(title: NotInvitedMessage, statusCode: StatusCodes.Status409Conflict);
}
