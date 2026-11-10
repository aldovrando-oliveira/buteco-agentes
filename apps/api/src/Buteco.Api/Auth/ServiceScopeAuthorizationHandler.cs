using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Buteco.Api.Identity.Validation;
using Buteco.Api.Users;
using Microsoft.AspNetCore.Routing;

namespace Buteco.Api.Auth;

// Requisito adicional aplicado a toda requisição autenticada. Duas tabelas
// explícitas: a de subjects de serviço (design.md da change catalogo-base-sincronizada,
// D6), em que cada subject de serviço conhecido passa só nas rotas da sua lista, e a
// de papéis (RoleRoutes; design.md da change papeis-no-token, D2), em que o token do
// painel (subject "operator") passa só nas rotas do papel dele. QUALQUER outro subject
// recebe 403, mesmo com um token estruturalmente válido.
//
// Até a #102 a regra era o inverso — "quem não é service:inbox passa" —, e
// um token assinado com a chave compartilhada para um subject novo teria o
// acesso inteiro do operador. A recusa por padrão é o que fecha isso. Até a #184,
// "operator" passava em tudo, inclusive nas rotas de serviço; agora o papel decide,
// e as rotas de serviço não estão na tabela de papéis (Q8f).
public sealed class ServiceScopeRequirement : IAuthorizationRequirement;

public sealed class ServiceScopeAuthorizationHandler : AuthorizationHandler<ServiceScopeRequirement>
{
    public const string OperatorSubject = "operator";

    public const string InboxSubject = "service:inbox";

    public const string ConnectorsSubject = "service:connectors";

    /// <summary>
    /// A aplicação externa (design.md da change aplicacoes-externas-credencial, D1). Não é serviço do
    /// Buteco, mas para a autorização é o mesmo tipo de chamador: um subject sem papel, com a lista de
    /// rotas dele aqui. O subject sai do claim <c>buteco_app</c> que o emissor põe só para quem está no
    /// cadastro (<c>IssuerTokenAuthentication.MapPrincipal</c>).
    /// </summary>
    public const string ApplicationSubject = "application";

    /// <summary>
    /// O subject cuja lista pode ficar vazia, e só ele, até a #187: a checagem de boot
    /// (<see cref="RouteDeclarationValidation"/>) recusa lista vazia em qualquer outro.
    /// <b>Temporário:</b> a #187 acrescenta <c>POST /agents/{id}/a2a</c> à lista de
    /// <see cref="ApplicationSubject"/> e retira esta exceção no mesmo commit (registrado na #187).
    /// </summary>
    public const string SubjectAllowedEmpty = ApplicationSubject;

    /// <summary>
    /// As rotas de cada subject sem papel (os de serviço e a aplicação externa), casadas por método e
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

            // A aplicação externa (aplicacoes-externas-credencial, D2; Q2, decisão do dono de
            // 09/11/2026): VAZIA. O token dela autentica e recebe 403 em toda rota, inclusive na A2A,
            // até a #187 abrir POST /agents/{id}/a2a junto com o filtro por agente, no mesmo deploy.
            // Assim a rota A2A nunca fica aberta a uma aplicação sem o filtro, em qualquer ordem de
            // produção das duas. O guarda é ExternalApplicationAuthorizationTests.
            // ApplicationToken_OnEveryAuthenticatedRoute_ReturnsForbidden, que a #187 muda de asserção.
            [ApplicationSubject] = [],
        };

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ServiceScopeRequirement requirement)
    {
        var subject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (subject == OperatorSubject)
        {
            AuthorizeByRole(context, requirement);
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

    // O token do painel chega aqui sempre com um papel conhecido: sem ele, a autenticação já
    // falhou com 401 (IssuerTokenAuthentication). Sem endpoint (rota inexistente), passa, para a
    // resposta continuar 404 e não virar 403.
    private static void AuthorizeByRole(AuthorizationHandlerContext context, ServiceScopeRequirement requirement)
    {
        if (UserRoleText.TryRead(context.User.FindFirst(IssuerTokenAuthentication.RoleClaim)?.Value) is not { } role ||
            context.Resource is not HttpContext httpContext)
        {
            return;
        }

        if (httpContext.GetEndpoint() is not RouteEndpoint endpoint ||
            RoleRoutes.Allows(httpContext.Request.Method, endpoint.RoutePattern.RawText ?? string.Empty, role))
        {
            context.Succeed(requirement);
        }
    }
}
