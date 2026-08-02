using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Providers.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class ProviderEndpointsTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ListProviders_ProviderWithApiKeyConfigured_AppearsWithItsModels()
    {
        // OpenAI:ApiKey vem configurada de appsettings.Development.json (não
        // é limpa por ApiFactoryFixture, ao contrário de Anthropic/Gemini).
        var response = await _client.GetAsync("/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var providers = await response.Content.ReadFromJsonAsync<List<ProviderResponse>>();
        Assert.NotNull(providers);

        var openAi = Assert.Single(providers!, provider => provider.Id == "openai");
        Assert.NotEmpty(openAi.Models);
    }

    [Fact]
    public async Task ListProviders_ProviderWithoutApiKeyConfigured_DoesNotAppear()
    {
        // Nem Anthropic nem Gemini estão configurados (ApiKey limpa
        // explicitamente por ApiFactoryFixture, independente do que
        // appsettings.Development.json tiver localmente).
        var response = await _client.GetAsync("/providers");

        var providers = await response.Content.ReadFromJsonAsync<List<ProviderResponse>>();
        Assert.NotNull(providers);
        Assert.DoesNotContain(providers!, provider => provider.Id == "anthropic");
        Assert.DoesNotContain(providers!, provider => provider.Id == "gemini");
    }
}
