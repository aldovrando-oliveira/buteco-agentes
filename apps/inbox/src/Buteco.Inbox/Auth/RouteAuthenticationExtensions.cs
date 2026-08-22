using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Buteco.Inbox.Auth;

// Mesmo molde de ChannelAdapterRegistrationExtensions.ValidateChannelAdapterRegistrations
// e de RouteAuthenticationExtensions equivalente em apps/api — checagem
// de integridade bidirecional entre o que está de fato mapeado no host
// e a allowlist declarada em código (design.md, Decision 4). Chamado
// depois de todos os app.Map* e antes de app.Run().
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
