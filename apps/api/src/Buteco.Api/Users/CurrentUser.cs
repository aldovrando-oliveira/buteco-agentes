using System.Security.Claims;
using Buteco.Api.Identity.Validation;
using Buteco.Api.Infrastructure;

namespace Buteco.Api.Users;

/// <summary>
/// Quem chama uma rota que age sobre "a própria pessoa" (design.md da change
/// verificacao-2-etapas-totp, D8 e D9): o usuário do <c>sub</c> do access token do emissor,
/// existente e ativo. O principal das rotas guarda o <c>sub</c> ao lado do subject da tabela de
/// clientes (<see cref="IssuerTokenAuthentication"/>). Um token de operador cujo <c>sub</c> não
/// é usuário não diz quem é a pessoa, e o access token de um usuário desativado vale até
/// 15 min. Nos dois casos não há pessoa a atender, e a rota responde <c>403</c>.
/// </summary>
public static class CurrentUser
{
    public const string ForbiddenMessage = "Esta rota exige o login de um usuário ativo.";

    public static async Task<User?> FindAsync(ClaimsPrincipal principal, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirst(IssuerTokenAuthentication.SubjectClaim)?.Value, out var userId))
        {
            return null;
        }

        var user = await dbContext.Users.FindAsync([userId], cancellationToken);
        return user is { IsActive: true } ? user : null;
    }

    public static IResult Forbidden() =>
        TypedResults.Problem(title: ForbiddenMessage, statusCode: StatusCodes.Status403Forbidden);
}
