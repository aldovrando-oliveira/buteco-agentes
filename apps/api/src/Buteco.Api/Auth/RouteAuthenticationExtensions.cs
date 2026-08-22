using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Buteco.Api.Auth;

// Mesmo molde de ChannelAdapterRegistrationExtensions.ValidateChannelAdapterRegistrations
// (apps/inbox) — checagem de integridade bidirecional entre o que está
// de fato mapeado no host e a allowlist declarada em código
// (design.md, Decision 4). Chamado depois de todos os app.Map* e antes
// de app.Run().
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

        // Toda rota AllowAnonymous precisa ter um motivo documentado —
        // pega quem abriu uma rota sem querer, sem justificativa.
        foreach (var endpoint in anonymousEndpoints)
        {
            if (endpoint.Metadata.GetMetadata<AnonymousRouteClassification>() is null)
            {
                problems.Add(
                    $"'{endpoint.RoutePattern.RawText}' está marcada AllowAnonymous sem AnonymousRouteClassification associada");
            }
        }

        // Toda rota esperada-anônima precisa existir de fato como
        // endpoint mapeado com AllowAnonymous — pega allowlist
        // desatualizada (rota removida/renomeada sem atualizar a lista).
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
