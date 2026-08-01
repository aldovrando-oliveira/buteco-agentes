using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Providers.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class NoProvidersConfiguredTests(NoProvidersConfiguredFixture factory) : IClassFixture<NoProvidersConfiguredFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ListProviders_NoProviderConfigured_ReturnsEmptyListWithoutError()
    {
        var response = await _client.GetAsync("/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var providers = await response.Content.ReadFromJsonAsync<List<ProviderResponse>>();
        Assert.NotNull(providers);
        Assert.Empty(providers!);
    }
}
