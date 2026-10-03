using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Buteco.Api.Auth;

// Requisito adicional aplicado a toda requisição autenticada. Uma tabela
// explícita de subjects (design.md da change catalogo-base-sincronizada, D6):
// "operator" passa em tudo; cada subject de serviço conhecido passa só nas
// rotas da sua lista; QUALQUER outro subject recebe 403, mesmo com um token
// estruturalmente válido.
//
// Até a #102 a regra era o inverso — "quem não é service:inbox passa" —, e
// um token assinado com a chave compartilhada para um subject novo teria o
// acesso inteiro do operador. A recusa por padrão é o que fecha isso.
public sealed class ServiceScopeRequirement : IAuthorizationRequirement;

public sealed class ServiceScopeAuthorizationHandler : AuthorizationHandler<ServiceScopeRequirement>
{
    public const string OperatorSubject = "operator";

    public const string InboxSubject = "service:inbox";

    public const string ConnectorsSubject = "service:connectors";

    /// <summary>
    /// As rotas de cada subject de serviço, casadas por método e
    /// <see cref="RoutePattern.RawText"/>. Conferidas no boot contra os
    /// endpoints mapeados (<see cref="ServiceScopeRouteValidation"/>): um padrão
    /// renomeado sem atualizar a lista derruba o boot em vez de virar 403 em
    /// produção.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> ServiceRoutes { get; } =
        new Dictionary<string, IReadOnlyList<(string Method, string Pattern)>>
        {
            // As duas rotas que apps/inbox de fato consome.
            [InboxSubject] =
            [
                ("POST", "/agents/{id}/a2a"),
                ("GET", "/agents/{id:guid}"),
            ],

            // As cinco rotas de sincronização (D8). Nenhuma rota do operador.
            //
            // A primeira tem barra final porque é o RawText que o MapGroup gera para
            // MapGet("/") (e também para MapGet("")), medido. Foi a checagem de boot
            // que pegou a primeira redação, sem a barra: sem ela, o conector
            // receberia 403 nesta rota em produção.
            [ConnectorsSubject] =
            [
                ("GET", "/sync/knowledge-bases/"),
                ("GET", "/sync/knowledge-bases/{knowledgeBaseId:guid}/documents"),
                ("PUT", "/sync/knowledge-bases/{knowledgeBaseId:guid}/documents"),
                ("DELETE", "/sync/knowledge-bases/{knowledgeBaseId:guid}/documents"),
                ("POST", "/sync/knowledge-bases/{knowledgeBaseId:guid}/sync-results"),
            ],
        };

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ServiceScopeRequirement requirement)
    {
        var subject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (subject == OperatorSubject)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Subject fora da tabela: não há lista, e não autoriza nada.
        if (subject is null || !ServiceRoutes.TryGetValue(subject, out var allowedRoutes))
        {
            return Task.CompletedTask;
        }

        if (context.Resource is HttpContext httpContext &&
            httpContext.GetEndpoint() is RouteEndpoint endpoint)
        {
            var method = httpContext.Request.Method;
            var pattern = endpoint.RoutePattern.RawText;

            var isAllowed = allowedRoutes.Any(
                route => string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase) && route.Pattern == pattern);

            if (isAllowed)
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
