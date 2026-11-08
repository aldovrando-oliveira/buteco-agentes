using Buteco.Api.Infrastructure;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// "Redefinir verificação" do admin, <c>POST /users/{id}/two-factor/reset</c> (design.md da
/// change verificacao-2-etapas-totp, D9). A operação é a do <see cref="TwoFactorReset"/>, a mesma
/// do comando de emergência: o cookie em voo é recusado no authorize, a renovação seguinte de toda
/// sessão da pessoa recebe <c>invalid_grant</c>, a lista de sessões fica vazia na hora, e o login
/// seguinte pede o cadastro.
///
/// <list type="bullet">
/// <item><b>Quem chama é o usuário real do token</b> (<see cref="CurrentUser"/>): um token de
/// operador sem usuário não diz quem é a pessoa, e sem isso a recusa de si mesmo não teria como ser
/// conferida;</item>
/// <item><b>a si mesmo, <c>403</c>:</b> a verificação é obrigatória e a própria pessoa não a
/// remove (design.md da change verificacao-2-etapas-obrigatoria, D5); quem troca de celular pede a
/// outro usuário, ou usa o comando de emergência. Sem a recusa, um access token roubado tiraria a
/// verificação sem código;</item>
/// <item><b>sem a verificação ligada, <c>409</c></b>, sem trocar o stamp à toa. O protótipo
/// desabilita o botão nesse estado.</item>
/// </list>
///
/// Na M1, sem papéis, qualquer usuário redefine a de outro (#184 restringe).
/// </summary>
public static class UserTwoFactorResetEndpoint
{
    public const string Path = "/users/{id:guid}/two-factor/reset";

    public const string SelfMessage =
        "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor.";

    public static IEndpointRouteBuilder MapUserTwoFactorResetEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost(Path, ResetAsync);
        return app;
    }

    private static async Task<IResult> ResetAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, TwoFactorReset reset, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        if (caller.Id == id)
        {
            return TypedResults.Problem(
                title: SelfMessage,
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

        await reset.ResetAsync(target, cancellationToken);

        return TypedResults.NoContent();
    }
}
