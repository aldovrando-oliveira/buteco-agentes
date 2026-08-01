using Buteco.Api.Providers.Queries.ListProviders;
using Buteco.Api.Providers.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.Providers.Endpoints;

public static class ProviderEndpoints
{
    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/providers", ListProvidersAsync);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<ProviderResponse>>> ListProvidersAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var providers = await mediator.Send(new ListProvidersQuery(), cancellationToken);

        return TypedResults.Ok(providers);
    }
}
