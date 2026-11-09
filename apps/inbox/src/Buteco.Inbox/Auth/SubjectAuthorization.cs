using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Buteco.Inbox.Auth;

// Tabela de subjects do apps/inbox (change inbox-restricao-de-subject, D1/D2):
// "operator" é o único subject aceito nas rotas autenticadas; QUALQUER outro subject recebe
// 403, mesmo com um token validamente assinado — inclusive "service:inbox", que este app usa
// para chamar o apps/api, e "service:connectors". Até a #116 a política era só "autenticado
// passa", e um token de serviço vazado tinha o acesso inteiro do operador aqui.
//
// Desde a change papeis-no-token (D2), o "operator" passa só nas rotas da RoleRoutes cujo papel
// ele traz (role, PanelRoles). Até a #184, ele passava em toda rota autenticada.
//
// Nenhum serviço chama rota autenticada do apps/inbox hoje, então não há lista de
// rotas por subject de serviço. O serviço que precisar de uma traz a lista e a entrada dela na
// checagem de boot (RouteDeclarationValidation), no molde do par no apps/api, sem libs/ (D3/D4):
// apps/api/src/Buteco.Api/Auth/ServiceScopeAuthorizationHandler.cs (tabela e regra) e
// apps/api/src/Buteco.Api/Auth/RouteDeclarationValidation.cs (checagem de boot).
public static class SubjectAuthorization
{
    // Precisa ser o mesmo texto que o login do apps/api emite
    // (ServiceScopeAuthorizationHandler.OperatorSubject, lá). Quem prova o
    // acordo é tests/InboxOrchestratorRoundTrip.Tests.
    public const string OperatorSubject = "operator";

    // FallbackPolicy: nenhuma rota do apps/inbox declara autorização própria, e é ela que governa
    // toda rota sem AllowAnonymous.
    public static AuthorizationPolicy BuildFallbackPolicy() =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PanelRoleRequirement())
            .Build();
}

public sealed class PanelRoleRequirement : IAuthorizationRequirement;

// O subject "operator" (comparado com StringComparer.Ordinal: "Operator" não passa) e o papel do
// token na entrada da rota. O token do painel chega aqui sempre com um papel conhecido: sem ele, a
// autenticação já falhou com 401 (IssuerTokenAuthentication). Sem endpoint (rota inexistente),
// passa, para a resposta continuar 404 e não virar 403.
public sealed class PanelRoleAuthorizationHandler : AuthorizationHandler<PanelRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PanelRoleRequirement requirement)
    {
        var role = context.User.FindFirst(PanelRoles.Claim)?.Value;
        if (!string.Equals(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, SubjectAuthorization.OperatorSubject, StringComparison.Ordinal) ||
            !PanelRoles.IsKnown(role) ||
            context.Resource is not HttpContext httpContext)
        {
            return Task.CompletedTask;
        }

        if (httpContext.GetEndpoint() is not RouteEndpoint endpoint ||
            (RoleRoutes.Table.TryGetValue((httpContext.Request.Method.ToUpperInvariant(), endpoint.RoutePattern.RawText ?? string.Empty), out var roles) &&
             roles.Contains(role!)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
