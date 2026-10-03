using Microsoft.AspNetCore.Routing;

namespace Buteco.Api.Auth;

/// <summary>
/// Checagem de integridade no boot (convenção 8; design.md da change
/// catalogo-base-sincronizada, D6): cada par (método, padrão) das listas de rotas
/// dos subjects de serviço precisa corresponder a um endpoint mapeado. A lista casa
/// por <see cref="Microsoft.AspNetCore.Routing.Patterns.RoutePattern.RawText"/>, e
/// uma rota renomeada sem atualizar a lista deixaria o serviço recebendo 403 em
/// produção sem nenhum teste de boot reprovar.
/// </summary>
/// <remarks>
/// Forma sobre o host construído, como
/// <see cref="RouteAuthenticationExtensions.ValidateRouteAuthenticationClassification"/>:
/// chamada depois de todos os <c>app.Map*</c> e antes de <c>app.Run()</c>.
/// </remarks>
public static class ServiceScopeRouteValidation
{
    public static void ValidateServiceScopeRoutes(
        this IEndpointRouteBuilder app,
        IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> serviceRoutes)
    {
        var problems = FindUnmappedRoutes(app.DataSources.SelectMany(dataSource => dataSource.Endpoints), serviceRoutes);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Lista de rotas de subject de serviço desatualizada: {string.Join("; ", problems)}.");
        }
    }

    /// <summary>
    /// O par sem rota de cada entrada, sobre um conjunto qualquer de endpoints. Separado
    /// da extensão para o teste da composição real passar os endpoints do host
    /// construído (<c>EndpointDataSource</c>), e não uma lista montada no teste.
    /// </summary>
    public static IReadOnlyList<string> FindUnmappedRoutes(
        IEnumerable<Endpoint> mappedEndpoints,
        IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> serviceRoutes)
    {
        var endpoints = mappedEndpoints.OfType<RouteEndpoint>().ToList();
        var problems = new List<string>();

        foreach (var (subject, routes) in serviceRoutes)
        {
            foreach (var (method, pattern) in routes)
            {
                var mapped = endpoints.Any(endpoint =>
                    endpoint.RoutePattern.RawText == pattern &&
                    AcceptsMethod(endpoint, method));

                if (!mapped)
                {
                    problems.Add($"'{subject}' lista {method} '{pattern}', que não corresponde a nenhum endpoint mapeado");
                }
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
