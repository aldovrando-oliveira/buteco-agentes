using Microsoft.Extensions.Configuration;

namespace Buteco.ProviderCatalog.Tests;

public class LlmProvidersTests
{
    [Theory]
    [InlineData(LlmProviders.OpenAi, "ChatClient")]
    [InlineData(LlmProviders.Anthropic, "Anthropic")]
    [InlineData(LlmProviders.Gemini, "Gemini")]
    public void IsConfigured_WhenApiKeyIsSet_ReturnsTrue(string providerId, string sectionName)
    {
        var provider = LlmProviders.All.Single(p => p.Id == providerId);
        var configuration = BuildConfiguration((sectionName + ":ApiKey", "some-key"));

        Assert.True(provider.IsConfigured(configuration));
    }

    [Theory]
    [InlineData(LlmProviders.OpenAi, "ChatClient")]
    [InlineData(LlmProviders.Anthropic, "Anthropic")]
    [InlineData(LlmProviders.Gemini, "Gemini")]
    public void IsConfigured_WhenApiKeyIsMissing_ReturnsFalse(string providerId, string sectionName)
    {
        var provider = LlmProviders.All.Single(p => p.Id == providerId);
        var configuration = BuildConfiguration();

        Assert.False(provider.IsConfigured(configuration));

        // Seção existe, mas ApiKey vazio/whitespace também não conta como configurado.
        var configurationWithBlankKey = BuildConfiguration((sectionName + ":ApiKey", "   "));
        Assert.False(provider.IsConfigured(configurationWithBlankKey));
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] entries)
    {
        var dictionary = entries.ToDictionary(e => e.Key, e => (string?)e.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dictionary).Build();
    }
}
