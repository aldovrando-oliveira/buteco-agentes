using Buteco.Api.Providers.Responses;
using Mediator;

namespace Buteco.Api.Providers.Queries.ListProviders;

public sealed record ListProvidersQuery : IQuery<IReadOnlyList<ProviderResponse>>;
