using Buteco.Api.Identity.Federated;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.AccessLinks;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;
using Buteco.Api.Users.TwoFactor;
using Buteco.Api.Users.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.Management;

/// <summary>
/// <c>PUT /users/{id}/role</c> (design.md da change papeis-no-token, D6; spec <c>user-management</c>,
/// "Mudar o papel de um usuário"), só do Admin, pelo papel gravado.
/// <list type="bullet">
/// <item><b>Promover a Admin pede a senha de quem chama</b> (<see cref="CallerPassword"/>, Q5), pelo
/// motivo do convite (#181): um access token de Admin roubado não vira, sozinho, Admin permanente de
/// uma conta que o atacante já tem. Rebaixar não pede: só tira acesso;</item>
/// <item><b>sob o <c>pg_advisory_xact_lock</c> da desativação</b>
/// (<see cref="UserAdministration.AdvisoryLockKey"/>), relendo dentro dele quem chama Admin ativo e
/// contando os Admins ativos. A desativação e a mudança de papel se cruzam no mesmo lock, e a
/// instalação nunca fica sem Admin ativo;</item>
/// <item><b>a si mesmo é permitido</b> (Q4), enquanto houver outro Admin ativo: é o único caminho em
/// que a contagem decide. Na desativação, quem chama não desativa a si mesmo, e a regra decorre;</item>
/// <item><b>não troca o stamp nem revoga sessões</b>: a renovação seguinte já traz o papel novo, e as
/// rotas de Admin o leem do banco a cada requisição (D3).</item>
/// </list>
/// </summary>
public static class UserRoleEndpoint
{
    public const string LastAdminMessage = "A instalação precisa de pelo menos um Admin ativo.";

    public sealed record ChangeRoleRequest(string? Papel, string? Senha, string? Codigo = null);

    public static async Task<IResult> ChangeAsync(
        Guid id,
        ChangeRoleRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        TwoFactorVerifier verifier,
        UserSessions sessions,
        TimeProvider timeProvider,
        FederatedProviderCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        if (UserRoleText.TryReadWire(request.Papel) is not { } role)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["papel"] = ["O papel precisa ser admin, operador ou leitor."],
            });
        }

        if (await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id, cancellationToken) is not { } current)
        {
            return UserFields.NotFound();
        }

        if (current.Status == UserStatus.Deactivated)
        {
            return TypedResults.Problem(title: "Reative o usuário antes de mudar o papel.", statusCode: StatusCodes.Status409Conflict);
        }

        if (current.Role == role)
        {
            return await UserEndpoints.DetailAsync(current, dbContext, sessions, timeProvider, catalog, cancellationToken);
        }

        if (role == UserRole.Admin)
        {
            if (await LoginThrottleResponses.LockedAsync(httpContext, throttle, caller, cancellationToken) is { } locked)
            {
                return locked;
            }

            if (await CallerPassword.RejectAsync(caller, request.Senha, request.Codigo, hasher, verifier, throttle, cancellationToken) is { } rejected)
            {
                return rejected;
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await UserAdministration.LockAsync(dbContext, cancellationToken);

        // Dentro do lock: quem chama pode ter sido rebaixado ou desativado por uma mudança que acabou
        // de confirmar. Sem esta releitura, os dois únicos Admins se rebaixariam um ao outro.
        if (!await UserAdministration.IsActiveAdminAsync(dbContext, caller.Id, cancellationToken))
        {
            return CurrentUser.Forbidden();
        }

        if (await dbContext.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken) is not { } target)
        {
            return UserFields.NotFound();
        }

        if (target.Role == UserRole.Admin && role != UserRole.Admin && target.IsActive &&
            await UserAdministration.CountActiveAdminsAsync(dbContext, cancellationToken) <= 1)
        {
            return TypedResults.Problem(title: LastAdminMessage, statusCode: StatusCodes.Status409Conflict);
        }

        target.ChangeRole(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await UserEndpoints.DetailAsync(target, dbContext, sessions, timeProvider, catalog, cancellationToken);
    }
}
