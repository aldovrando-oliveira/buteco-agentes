using Microsoft.AspNetCore.Authorization;

namespace Buteco.Connectors.Auth;

/// <summary>
/// Checagem de integridade no boot (convenção 8; design.md, D1), nos dois sentidos:
/// <list type="bullet">
/// <item>toda entrada (método, padrão) da tabela corresponde a um endpoint mapeado, o
/// sentido da D6 da #102;</item>
/// <item>todo endpoint autenticado mapeado aparece na lista de algum subject. No
/// <c>apps/api</c> esse sentido não precisa existir, porque o operador cobre a rota
/// esquecida; aqui ela daria <c>403</c> a todo mundo.</item>
/// </list>
/// Rotas anônimas ficam fora dos dois sentidos: quem as confere é
/// <see cref="RouteAuthenticationExtensions"/>.
/// </summary>
/// <remarks>Forma sobre o host construído, depois de todos os <c>Map*</c>.</remarks>
public static class ConnectorsSubjectRouteValidation
{
    public static void ValidateConnectorsSubjectRoutes(
        this IEndpointRouteBuilder app,
        IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> subjectRoutes)
    {
        var problems = FindProblems(app.DataSources.SelectMany(dataSource => dataSource.Endpoints), subjectRoutes);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Tabela de subjects divergente das rotas mapeadas: {string.Join("; ", problems)}.");
        }
    }

    /// <summary>
    /// Separado da extensão para o teste da composição real passar os endpoints do host
    /// construído, e não uma lista montada no teste.
    /// </summary>
    public static IReadOnlyList<string> FindProblems(
        IEnumerable<Endpoint> mappedEndpoints,
        IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> subjectRoutes)
    {
        var endpoints = mappedEndpoints.OfType<RouteEndpoint>().ToList();
        var problems = new List<string>();

        foreach (var (subject, routes) in subjectRoutes)
        {
            foreach (var (method, pattern) in routes)
            {
                if (!endpoints.Any(endpoint => endpoint.RoutePattern.RawText == pattern && AcceptsMethod(endpoint, method)))
                {
                    problems.Add($"'{subject}' lista {method} '{pattern}', que não corresponde a nenhum endpoint mapeado");
                }
            }
        }

        var listed = subjectRoutes.Values.SelectMany(routes => routes).ToList();
        foreach (var endpoint in endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            var owned = listed.Any(route =>
                route.Pattern == endpoint.RoutePattern.RawText &&
                (methods.Count == 0 || methods.Contains(route.Method, StringComparer.OrdinalIgnoreCase)));

            if (!owned)
            {
                problems.Add($"'{endpoint.RoutePattern.RawText}' exige autenticação e não está na lista de nenhum subject");
            }
        }

        return problems;
    }

    // Endpoint sem HttpMethodMetadata aceita qualquer método.
    private static bool AcceptsMethod(RouteEndpoint endpoint, string method)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        return methods is null || methods.Count == 0 ||
            methods.Any(candidate => string.Equals(candidate, method, StringComparison.OrdinalIgnoreCase));
    }
}
