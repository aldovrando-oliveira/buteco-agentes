using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Buteco.Api.Auth;

// Requisito adicional aplicado a toda requisição autenticada
// (design.md, Decision 3): tokens com sub != "service:inbox" (isto é,
// o operador) passam sempre. Tokens com sub == "service:inbox" só são
// autorizados nas duas rotas que apps/inbox de fato consome — qualquer
// outra responde 403 Forbidden, mesmo com um token estruturalmente
// válido.
public sealed class ServiceScopeRequirement : IAuthorizationRequirement;

public sealed class ServiceScopeAuthorizationHandler : AuthorizationHandler<ServiceScopeRequirement>
{
    public const string ServiceSubject = "service:inbox";

    private static readonly (string Method, string Pattern)[] AllowedServiceRoutes =
    [
        ("POST", "/agents/{id}/a2a"),
        ("GET", "/agents/{id:guid}"),
    ];

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ServiceScopeRequirement requirement)
    {
        var subject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (subject != ServiceSubject)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.Resource is HttpContext httpContext &&
            httpContext.GetEndpoint() is RouteEndpoint endpoint)
        {
            var method = httpContext.Request.Method;
            var pattern = endpoint.RoutePattern.RawText;

            var isAllowed = Array.Exists(
                AllowedServiceRoutes,
                route => string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase) && route.Pattern == pattern);

            if (isAllowed)
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
