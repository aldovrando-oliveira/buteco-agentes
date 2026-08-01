using Buteco.Api.Providers.Responses;
using Mediator;

namespace Buteco.Api.Providers.Queries.ListProviders;

public sealed class ListProvidersQueryHandler(ProviderCatalogService catalogService)
    : IQueryHandler<ListProvidersQuery, IReadOnlyList<ProviderResponse>>
{
    public ValueTask<IReadOnlyList<ProviderResponse>> Handle(ListProvidersQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(catalogService.GetAvailableProviders());
}
