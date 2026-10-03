using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Buteco.Connectors.Auth;

// Cópia do apps/inbox: checagem de integridade bidirecional entre as rotas
// anônimas mapeadas e a allowlist declarada no Program.cs (convenção 8).
// Chamado depois de todos os app.Map* e antes de app.Run().
public static class RouteAuthenticationExtensions
{
    public static void ValidateRouteAuthenticationClassification(
        this IEndpointRouteBuilder app,
        params string[] expectedAnonymousRoutePatterns)
    {
        var endpoints = app.DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var anonymousEndpoints = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .ToList();

        var problems = new List<string>();

        foreach (var endpoint in anonymousEndpoints)
        {
            if (endpoint.Metadata.GetMetadata<AnonymousRouteClassification>() is null)
            {
                problems.Add(
                    $"'{endpoint.RoutePattern.RawText}' está marcada AllowAnonymous sem AnonymousRouteClassification associada");
            }
        }

        var mappedAnonymousPatterns = anonymousEndpoints
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet();

        foreach (var expected in expectedAnonymousRoutePatterns)
        {
            if (!mappedAnonymousPatterns.Contains(expected))
            {
                problems.Add(
                    $"'{expected}' está na allowlist esperada de rotas anônimas mas não corresponde a nenhum endpoint mapeado com AllowAnonymous");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Classificação de rotas de autenticação incompleta: {string.Join("; ", problems)}.");
        }
    }
}
