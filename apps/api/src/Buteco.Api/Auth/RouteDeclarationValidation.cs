using Buteco.Api.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Buteco.Api.Auth;

/// <summary>
/// Toda rota autenticada declara papel ou subject de serviço (convenção 8; design.md da change
/// papeis-no-token, D2; spec <c>route-roles</c>), conferido no boot, sobre os endpoints do host
/// construído, nos três sentidos:
/// <list type="number">
/// <item>todo <see cref="RouteEndpoint"/> que exige autenticação está na <see cref="RoleRoutes"/>
/// ou na lista de um subject de serviço, para cada método que ele aceita. Sem isso, a rota nova
/// responderia <c>403</c> a todo mundo, em silêncio;</item>
/// <item>toda entrada das duas tabelas tem rota mapeada com o método. Sem isso, uma rota renomeada
/// deixaria a entrada antiga viva e a nova sem papel;</item>
/// <item>nenhuma entrada da <see cref="RoleRoutes"/> tem lista de papéis vazia;</item>
/// <item>nenhum subject sem papel tem lista de rotas vazia, sem exceção (a da aplicação externa, que a
/// #186 deixou até a rota A2A abrir, saiu com a change agentes-por-aplicacao, D2.1), e o subject <see cref="ServiceScopeAuthorizationHandler.ApplicationSubject"/> existe (design.md da change
/// aplicacoes-externas-credencial, D1). Uma lista esvaziada por engano deixaria o serviço recebendo
/// <c>403</c> em tudo, e sem a chave da aplicação o token dela não teria a lista que a autoriza.</item>
/// </list>
/// Forma sobre o host construído, como <see cref="ServiceScopeRouteValidation"/>: chamada depois de
/// todos os <c>app.Map*</c> e antes de <c>app.Run()</c>.
/// </summary>
public static class RouteDeclarationValidation
{
    public static void ValidateRouteDeclarations(
        this IEndpointRouteBuilder app,
        IReadOnlyDictionary<(string Method, string Pattern), IReadOnlyList<UserRole>> roleRoutes,
        IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> serviceRoutes)
    {
        var problems = FindProblems(app.DataSources.SelectMany(dataSource => dataSource.Endpoints), roleRoutes, serviceRoutes);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Declaração de papel ou de subject das rotas incompleta: {string.Join("; ", problems)}.");
        }
    }

    /// <summary>
    /// As divergências, sobre um conjunto qualquer de endpoints. Separado da extensão para o teste da
    /// composição real passar os endpoints do host construído, e não uma lista montada no teste.
    /// </summary>
    public static IReadOnlyList<string> FindProblems(
        IEnumerable<Endpoint> mappedEndpoints,
        IReadOnlyDictionary<(string Method, string Pattern), IReadOnlyList<UserRole>> roleRoutes,
        IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> serviceRoutes)
    {
        var endpoints = mappedEndpoints.OfType<RouteEndpoint>().ToList();
        var declared = roleRoutes.Keys
            .Concat(serviceRoutes.Values.SelectMany(routes => routes))
            .Select(route => (Method: route.Method.ToUpperInvariant(), route.Pattern))
            .ToHashSet();
        var problems = new List<string>();

        foreach (var endpoint in endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var pattern = endpoint.RoutePattern.RawText ?? string.Empty;
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (methods is null || methods.Count == 0)
            {
                problems.Add($"'{pattern}' é autenticada e aceita qualquer método, e a declaração é por método");
                continue;
            }

            foreach (var method in methods.Select(method => method.ToUpperInvariant()))
            {
                if (!declared.Contains((method, pattern)))
                {
                    problems.Add($"{method} '{pattern}' é autenticada e não está na tabela de papéis nem na lista de nenhum subject de serviço");
                }
            }
        }

        foreach (var ((method, pattern), roles) in roleRoutes)
        {
            if (roles.Count == 0)
            {
                problems.Add($"a tabela de papéis lista {method} '{pattern}' sem nenhum papel");
            }

            if (!endpoints.Any(endpoint => endpoint.RoutePattern.RawText == pattern && AcceptsMethod(endpoint, method)))
            {
                problems.Add($"a tabela de papéis lista {method} '{pattern}', que não corresponde a nenhum endpoint mapeado");
            }
        }

        foreach (var (subject, routes) in serviceRoutes)
        {
            if (routes.Count == 0)
            {
                problems.Add($"o subject '{subject}' tem a lista de rotas vazia");
            }
        }

        if (!serviceRoutes.ContainsKey(ServiceScopeAuthorizationHandler.ApplicationSubject))
        {
            problems.Add($"o subject '{ServiceScopeAuthorizationHandler.ApplicationSubject}', da aplicação externa, não está na lista de subjects");
        }

        problems.AddRange(ServiceScopeRouteValidation.FindUnmappedRoutes(endpoints, serviceRoutes));
        return problems;
    }

    private static bool AcceptsMethod(RouteEndpoint endpoint, string method)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        return methods is null || methods.Count == 0 ||
            methods.Any(candidate => string.Equals(candidate, method, StringComparison.OrdinalIgnoreCase));
    }
}
