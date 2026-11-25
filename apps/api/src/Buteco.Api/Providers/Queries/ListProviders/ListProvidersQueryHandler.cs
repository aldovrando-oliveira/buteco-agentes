using Buteco.Api.Providers.Responses;
using Mediator;

namespace Buteco.Api.Providers.Queries.ListProviders;

public sealed class ListProvidersQueryHandler(ProviderCatalogService catalogService)
    : IQueryHandler<ListProvidersQuery, IReadOnlyList<ProviderResponse>>
{
    public async ValueTask<IReadOnlyList<ProviderResponse>> Handle(ListProvidersQuery query, CancellationToken cancellationToken) =>
        await catalogService.GetAvailableProvidersAsync(cancellationToken);
}
