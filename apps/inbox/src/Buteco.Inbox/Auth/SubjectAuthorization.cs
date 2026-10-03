using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Buteco.Inbox.Auth;

// Tabela de subjects do apps/inbox (change inbox-restricao-de-subject, D1/D2):
// "operator" passa em toda rota autenticada; QUALQUER outro subject recebe 403,
// mesmo com um token validamente assinado com a chave compartilhada — inclusive
// "service:inbox", que este app assina para chamar o apps/api, e
// "service:connectors". Até a #116 a política era só "autenticado passa", e um
// token de serviço vazado tinha o acesso inteiro do operador aqui.
//
// Nenhum serviço chama rota autenticada do apps/inbox hoje, então não há lista de
// rotas por subject de serviço nem checagem de boot dela. O serviço que precisar
// de uma traz as duas, no molde do par no apps/api, sem libs/ (D3/D4):
// apps/api/src/Buteco.Api/Auth/ServiceScopeAuthorizationHandler.cs (tabela e
// regra), apps/api/src/Buteco.Api/Auth/ServiceScopeRouteValidation.cs (checagem
// de boot) e apps/api/src/Buteco.Api/Program.cs (AddAuthorization e
// ValidateServiceScopeRoutes). A lista casa por RoutePattern.RawText, e
// MapGroup("/channels") com MapGet("/") gera "/channels/", com barra final.
public static class SubjectAuthorization
{
    // Precisa ser o mesmo texto que o login do apps/api emite
    // (ServiceScopeAuthorizationHandler.OperatorSubject, lá). Quem prova o
    // acordo é tests/InboxOrchestratorRoundTrip.Tests.
    public const string OperatorSubject = "operator";

    // FallbackPolicy: nenhuma rota do apps/inbox declara autorização própria, e
    // é ela que governa toda rota sem AllowAnonymous. RequireClaim compara o
    // valor com StringComparer.Ordinal: "Operator" não passa.
    public static AuthorizationPolicy BuildFallbackPolicy() =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimTypes.NameIdentifier, OperatorSubject)
            .Build();
}
