using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// "Redefinir verificação" do admin, <c>POST /users/{id}/two-factor/reset</c> (design.md da
/// change verificacao-2-etapas-totp, D9). Apaga o segredo, o cadastro pendente, o último passo
/// e os códigos de recuperação, troca o <c>security_stamp</c> e revoga as autorizações da
/// pessoa, numa transação: o cookie em voo é recusado no authorize, a renovação seguinte de toda
/// sessão dela recebe <c>invalid_grant</c>, e a lista de sessões fica vazia na hora. Sem a
/// revogação, a sessão morta pelo stamp apareceria viva por até 14 dias (design.md da change
/// rotas-conta-usuarios, D2: toda troca do stamp revoga as autorizações).
///
/// <list type="bullet">
/// <item><b>Quem chama é o usuário real do token</b> (<see cref="CurrentUser"/>): um token de
/// operador sem usuário não diz quem é a pessoa, e sem isso a recusa de si mesmo não teria como ser
/// conferida;</item>
/// <item><b>a si mesmo, <c>403</c>:</b> a própria pessoa remove a dela com um código. Sem a
/// recusa, um access token roubado tiraria a verificação sem código;</item>
/// <item><b>sem a verificação ligada, <c>409</c></b>, sem trocar o stamp à toa. O protótipo
/// desabilita o botão nesse estado.</item>
/// </list>
///
/// Na M1, sem papéis, qualquer usuário redefine a de outro (#184 restringe).
/// </summary>
public static class UserTwoFactorResetEndpoint
{
    public const string Path = "/users/{id:guid}/two-factor/reset";

    public static IEndpointRouteBuilder MapUserTwoFactorResetEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost(Path, ResetAsync);
        return app;
    }

    private static async Task<IResult> ResetAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, UserSessions sessions, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        if (caller.Id == id)
        {
            return TypedResults.Problem(
                title: "Para remover a sua verificação, use Minha conta, com um código.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var target = await dbContext.Users.FindAsync([id], cancellationToken);
        if (target is null)
        {
            return TypedResults.Problem(title: "Usuário não encontrado.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!target.IsTwoFactorEnabled)
        {
            return TypedResults.Problem(title: "A verificação em 2 etapas deste usuário não está configurada.", statusCode: StatusCodes.Status409Conflict);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        target.ResetTwoFactor();
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.UserRecoveryCodes.Where(code => code.UserId == target.Id).ExecuteDeleteAsync(cancellationToken);
        await sessions.EndAllAsync(target.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.NoContent();
    }
}
