using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Buteco.Inbox.Auth;

// Toda rota autenticada do apps/inbox declara os papéis do painel (convenção 8; design.md da
// change papeis-no-token, D2; spec route-roles), conferido no boot sobre os endpoints do host
// construído, nos três sentidos: todo endpoint autenticado está na RoleRoutes; toda entrada tem
// rota com o método; nenhuma entrada tem lista vazia. É a checagem que este app não tinha: até a
// #184, toda rota autenticada era do operator por política, sem lista. Copiada do apps/api, sem
// libs/, sem a tabela de subjects de serviço, que aqui não existe.
public static class RouteDeclarationValidation
{
    public static void ValidateRouteDeclarations(
        this IEndpointRouteBuilder app,
        IReadOnlyDictionary<(string Method, string Pattern), IReadOnlyList<string>> roleRoutes)
    {
        var problems = FindProblems(app.DataSources.SelectMany(dataSource => dataSource.Endpoints), roleRoutes);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Declaração de papel das rotas incompleta: {string.Join("; ", problems)}.");
        }
    }

    public static IReadOnlyList<string> FindProblems(
        IEnumerable<Endpoint> mappedEndpoints,
        IReadOnlyDictionary<(string Method, string Pattern), IReadOnlyList<string>> roleRoutes)
    {
        var endpoints = mappedEndpoints.OfType<RouteEndpoint>().ToList();
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
                if (!roleRoutes.ContainsKey((method, pattern)))
                {
                    problems.Add($"{method} '{pattern}' é autenticada e não está na tabela de papéis");
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

        return problems;
    }

    private static bool AcceptsMethod(RouteEndpoint endpoint, string method)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        return methods is null || methods.Count == 0 ||
            methods.Any(candidate => string.Equals(candidate, method, StringComparison.OrdinalIgnoreCase));
    }
}
