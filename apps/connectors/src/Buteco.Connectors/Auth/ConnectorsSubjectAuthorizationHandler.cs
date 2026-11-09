using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Buteco.Connectors.Auth;

/// <summary>Requisito aplicado a toda requisição autenticada, junto da autenticação.</summary>
public sealed class ConnectorsSubjectRequirement : IAuthorizationRequirement;

/// <summary>
/// A tabela explícita de subjects do <c>apps/connectors</c> (design.md, D1). Cada
/// subject passa só nas rotas da sua lista; qualquer outro subject, inclusive
/// <c>service:inbox</c> e <c>service:connectors</c>, recebe <c>403</c>.
/// </summary>
/// <remarks>
/// <para>
/// Diferente do <c>apps/api</c>, aqui <c>operator</c> <b>não</b> passa em tudo: a
/// descrição de pasta existe para o <c>apps/api</c> validar a pasta na #104, e o
/// frontend não precisa dela.
/// </para>
/// <para>
/// Sem este requisito, a <c>FallbackPolicy</c> seria só
/// <c>RequireAuthenticatedUser()</c>, que é o defeito da #116 no <c>apps/inbox</c>.
/// </para>
/// </remarks>
public sealed class ConnectorsSubjectAuthorizationHandler : AuthorizationHandler<ConnectorsSubjectRequirement>
{
    public const string OperatorSubject = "operator";

    /// <summary>
    /// O <c>apps/api</c>, pelo token que ele gera no processo para o cliente
    /// <c>buteco-servico-api</c> (servicos-token-emissor, D4), mapeado pela tabela de
    /// clientes.
    /// </summary>
    public const string ApiSubject = "service:api";

    /// <summary>
    /// As rotas de cada subject, casadas por método e <c>RoutePattern.RawText</c>
    /// (medido na tarefa 1.3: <c>MapGroup("/connectors")</c> não acrescenta barra
    /// final a nenhuma das três). Conferidas no boot nos dois sentidos por
    /// <see cref="ConnectorsSubjectRouteValidation"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> SubjectRoutes { get; } =
        new Dictionary<string, IReadOnlyList<(string Method, string Pattern)>>
        {
            // A lista do painel é a da tabela de papéis (papeis-no-token, D2): o papel do token
            // decide, entre estas, onde ele passa.
            [OperatorSubject] = [.. RoleRoutes.Table.Keys],
            [ApiSubject] =
            [
                ("GET", "/connectors/providers/{providerKey}/folder"),
            ],
        };

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ConnectorsSubjectRequirement requirement)
    {
        var subject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Subject fora da tabela: não há lista, e não autoriza nada.
        if (subject is null || !SubjectRoutes.TryGetValue(subject, out var allowedRoutes))
        {
            return Task.CompletedTask;
        }

        if (context.Resource is HttpContext httpContext &&
            httpContext.GetEndpoint() is RouteEndpoint endpoint)
        {
            var method = httpContext.Request.Method;
            var pattern = endpoint.RoutePattern.RawText;

            if (allowedRoutes.Any(route =>
                    string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase) && route.Pattern == pattern) &&
                (subject != OperatorSubject || RoleAllows(context.User, method, pattern)))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }

    // O token do painel chega aqui sempre com um papel conhecido: sem ele, a autenticação já falhou
    // com 401 (IssuerTokenAuthentication). O papel tem de estar na entrada da rota.
    private static bool RoleAllows(ClaimsPrincipal user, string method, string? pattern) =>
        user.FindFirst(PanelRoles.Claim)?.Value is { } role &&
        RoleRoutes.Table.TryGetValue((method.ToUpperInvariant(), pattern ?? string.Empty), out var roles) &&
        roles.Contains(role);
}
