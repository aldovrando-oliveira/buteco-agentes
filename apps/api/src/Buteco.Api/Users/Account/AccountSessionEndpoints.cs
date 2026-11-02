using Buteco.Api.Identity.Validation;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Sessions;

namespace Buteco.Api.Users.Account;

/// <summary>
/// As sessões da própria pessoa, sob <c>/account/sessions</c> (design.md da change
/// rotas-conta-usuarios, D4). A sessão do token é a autorização do <c>oi_au_id</c> dele, que o
/// principal guarda (<see cref="IssuerTokenAuthentication.AuthorizationIdClaim"/>) e que não
/// muda entre renovações: ela sai marcada <c>atual</c>, e "encerrar as outras" a poupa.
/// Encerrar a própria sessão atual pela rota de uma é permitido: ela cai na renovação seguinte.
/// </summary>
public static class AccountSessionEndpoints
{
    public const string GroupPath = "/account/sessions";

    public sealed record SessionResponse(string Id, DateTimeOffset CriadaEm, DateTimeOffset UltimoUso, bool Atual);

    public sealed record EndedResponse(int Encerradas);

    public static IEndpointRouteBuilder MapAccountSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPath);
        group.MapGet("", ListAsync);
        group.MapPost("/{id}/revoke", RevokeAsync);
        group.MapPost("/revoke-others", RevokeOthersAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext, AppDbContext dbContext, UserSessions sessions, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return CurrentUser.Forbidden();
        }

        var current = CurrentAuthorizationId(httpContext);
        var live = await sessions.ListLiveAsync(user.Id, cancellationToken);
        return TypedResults.Ok(live.Select(session => new SessionResponse(session.Id, session.CriadaEm, session.UltimoUso, session.Id == current)).ToList());
    }

    private static async Task<IResult> RevokeAsync(
        string id, HttpContext httpContext, AppDbContext dbContext, UserSessions sessions, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return CurrentUser.Forbidden();
        }

        return await sessions.EndOneAsync(user.Id, id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.Problem(title: "Sessão não encontrada.", statusCode: StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> RevokeOthersAsync(
        HttpContext httpContext, AppDbContext dbContext, UserSessions sessions, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } user)
        {
            return CurrentUser.Forbidden();
        }

        // Sem a sessão do token, "as outras" seriam todas: recusar é melhor que encerrar a atual sem querer.
        if (CurrentAuthorizationId(httpContext) is not { } current)
        {
            return TypedResults.Problem(title: "Não foi possível identificar a sessão atual.", statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.Ok(new EndedResponse(await sessions.EndOthersAsync(user.Id, current, cancellationToken)));
    }

    private static string? CurrentAuthorizationId(HttpContext httpContext) =>
        httpContext.User.FindFirst(IssuerTokenAuthentication.AuthorizationIdClaim)?.Value;
}
