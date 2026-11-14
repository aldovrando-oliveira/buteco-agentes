using Buteco.Api.Identity.Federated;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.AccessLinks;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;
using Buteco.Api.Users.Sessions;
using Buteco.Api.Users.TwoFactor;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.Federated;

/// <summary>
/// Conectar e desconectar um provedor federado (design.md da change provedores-federados-google, D6), e a
/// rota só leitura dos provedores para a tela Identidade (D9).
///
/// <list type="bullet">
/// <item><b>conectar</b> é só da própria pessoa, com a confirmação de quem chama (<see cref="CallerPassword"/>:
/// a senha, ou o código do app de quem não tem senha), porque transforma um access token roubado em entrada
/// permanente; o retorno grava o vínculo (<see cref="FederatedLoginEndpoints"/>). O Admin nunca conecta a
/// conta de outro;</item>
/// <item><b>desconectar</b> não pede senha (tirar acesso não amplia nada), mas a última forma de entrada
/// não sai, conferida com a linha do usuário travada (<c>SELECT … FOR UPDATE</c>), na mesma transação da
/// remoção;</item>
/// <item><b>pelo Admin, troca o stamp e revoga as sessões</b> na mesma transação (Q2): a conta do provedor
/// pode ter sido comprometida, e as sessões não guardam a forma de entrada. Pela própria pessoa, não.</item>
/// </list>
/// </summary>
public static class FederatedAccountEndpoints
{
    public const string ConnectPath = "/account/federated/{chave}/connect";

    public const string DisconnectOwnPath = "/account/federated/{chave}";

    public const string DisconnectByAdminPath = "/users/{id:guid}/federated/{chave}";

    public const string ProvidersPath = "/identity/federated-providers";

    public const string LastFormMessage = "A última forma de entrada não pode ser removida.";

    public const string OwnByAdminMessage = "Para desconectar a sua própria conta, use Minha conta.";

    public const string NotConnectedMessage = "Esta conta não está conectada a esse provedor.";

    public sealed record ConnectRequest(string? Senha, string? Codigo);

    public sealed record IdentityProvider(
        string Chave, string Tipo, string Nome, string Issuer, IReadOnlyList<string> UsadoPara, string ClientId, string UriDeRetorno, int Vinculos);

    public sealed record IdentityProvidersResponse(IReadOnlyList<IdentityProvider> Provedores);

    public static IEndpointRouteBuilder MapFederatedAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ConnectPath, ConnectAsync);
        app.MapDelete(DisconnectOwnPath, DisconnectOwnAsync);
        app.MapDelete(DisconnectByAdminPath, DisconnectByAdminAsync);
        app.MapGet(ProvidersPath, ListProvidersAsync);
        return app;
    }

    private static async Task<IResult> ConnectAsync(
        string chave,
        ConnectRequest? request,
        HttpContext httpContext,
        AppDbContext dbContext,
        FederatedProviderCatalog catalog,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        TwoFactorVerifier verifier,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        // A chave antes da confirmação: um 404 não gasta o código do app de ninguém.
        if (catalog.Find(chave) is not { } provider)
        {
            return FederatedLoginEndpoints.NotConfigured();
        }

        if (await LoginThrottleResponses.LockedAsync(httpContext, throttle, caller, cancellationToken) is { } locked)
        {
            return locked;
        }

        if (await CallerPassword.RejectAsync(caller, request?.Senha, request?.Codigo, hasher, verifier, throttle, cancellationToken) is { } rejected)
        {
            return rejected;
        }

        return await FederatedChallenge.BeginAsync(httpContext, provider, new Dictionary<string, string?>
        {
            [FederatedLogin.Properties.Intent] = FederatedLogin.Intents.Connect,
            [FederatedLogin.Properties.UserId] = caller.Id.ToString(),
            [FederatedLogin.Properties.SecurityStamp] = caller.SecurityStamp,
        });
    }

    private static async Task<IResult> DisconnectOwnAsync(
        string chave,
        HttpContext httpContext,
        AppDbContext dbContext,
        FederatedProviderCatalog catalog,
        UserSessions sessions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        return await DisconnectAsync(caller.Id, caller.Id, chave, byAdmin: false, dbContext, catalog, sessions, loggerFactory, cancellationToken);
    }

    private static async Task<IResult> DisconnectByAdminAsync(
        Guid id,
        string chave,
        HttpContext httpContext,
        AppDbContext dbContext,
        FederatedProviderCatalog catalog,
        UserSessions sessions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        if (caller.Id == id)
        {
            return TypedResults.Problem(title: OwnByAdminMessage, statusCode: StatusCodes.Status403Forbidden);
        }

        return await DisconnectAsync(id, caller.Id, chave, byAdmin: true, dbContext, catalog, sessions, loggerFactory, cancellationToken);
    }

    private static async Task<IResult> DisconnectAsync(
        Guid userId,
        Guid byUserId,
        string chave,
        bool byAdmin,
        AppDbContext dbContext,
        FederatedProviderCatalog catalog,
        UserSessions sessions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (catalog.Find(chave) is not { } provider)
        {
            return NotConnected();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // A linha do usuário travada antes de contar: a conferência e a remoção na mesma transação.
        var locked = await dbContext.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM users WHERE id = {userId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (locked.Count == 0)
        {
            return TypedResults.Problem(title: "Usuário não encontrado.", statusCode: StatusCodes.Status404NotFound);
        }

        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == userId, cancellationToken);
        var links = await dbContext.UserFederatedIdentities.Where(link => link.UserId == userId).ToListAsync(cancellationToken);
        var issuer = provider.Issuer.AbsoluteUri;
        if (links.FirstOrDefault(link => link.Issuer == issuer) is not { } target)
        {
            return NotConnected();
        }

        if (FederatedSignInForms.Count(user.PasswordHash is not null, links.Where(link => link != target), catalog) == 0)
        {
            return TypedResults.Problem(title: LastFormMessage, statusCode: StatusCodes.Status409Conflict);
        }

        dbContext.UserFederatedIdentities.Remove(target);
        if (byAdmin)
        {
            user.RenewSecurityStamp();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (byAdmin)
        {
            await sessions.EndAllAsync(userId, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        FederatedLoginEndpoints.LogDisconnected(loggerFactory.CreateLogger(FederatedLoginEndpoints.LoggerCategory), userId, provider.Key, byUserId);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListProvidersAsync(
        HttpContext httpContext, AppDbContext dbContext, FederatedProviderCatalog catalog, FederatedRedirect redirect, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        var counts = await dbContext.UserFederatedIdentities.AsNoTracking()
            .GroupBy(link => link.Issuer)
            .Select(group => new { Issuer = group.Key, Users = group.Select(link => link.UserId).Distinct().Count() })
            .ToDictionaryAsync(row => row.Issuer, row => row.Users, cancellationToken);

        return TypedResults.Ok(new IdentityProvidersResponse(catalog.Providers.Select(provider => new IdentityProvider(
            provider.Key,
            provider.Type,
            provider.Name,
            provider.Issuer.AbsoluteUri,
            ["login"],
            provider.ClientId,
            redirect.RedirectUri.AbsoluteUri,
            counts.GetValueOrDefault(provider.Issuer.AbsoluteUri))).ToList()));
    }

    private static IResult NotConnected() =>
        TypedResults.Problem(title: NotConnectedMessage, statusCode: StatusCodes.Status404NotFound);
}
