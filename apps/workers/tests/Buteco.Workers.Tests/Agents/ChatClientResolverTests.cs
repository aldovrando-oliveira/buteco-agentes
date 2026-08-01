using Buteco.ProviderCatalog;
using Buteco.Workers.Agents;
using Buteco.Workers.Options;

namespace Buteco.Workers.Tests.Agents;

/// <summary>
/// Testa só "qual tipo de client é construído para qual provedor", sem
/// nenhuma chamada de rede real (Decision 7 do design.md da change
/// backend-multi-provedor-llm) — cada `Options` recebe uma chave "fake",
/// suficiente para a construção do client não lançar, mas nenhuma chamada
/// HTTP é feita nesses testes.
/// </summary>
public class ChatClientResolverTests
{
    [Fact]
    public void Resolve_OpenAi_BuildsOpenAiChatClient()
    {
        var resolver = CreateResolver(openAiApiKey: "fake-key");

        var chatClient = resolver.Resolve(LlmProviders.OpenAi, "gpt-5.6-sol");

        Assert.Equal("OpenAIChatClient", chatClient.GetType().Name);
    }

    [Fact]
    public void Resolve_Anthropic_BuildsAnthropicChatClient()
    {
        var resolver = CreateResolver(anthropicApiKey: "fake-key");

        var chatClient = resolver.Resolve(LlmProviders.Anthropic, "claude-opus-5");

        Assert.Equal("AnthropicChatClient", chatClient.GetType().Name);
    }

    [Fact]
    public void Resolve_Gemini_BuildsGoogleGenAIChatClient()
    {
        var resolver = CreateResolver(geminiApiKey: "fake-key");

        var chatClient = resolver.Resolve(LlmProviders.Gemini, "gemini-3.6-flash");

        Assert.Equal("GoogleGenAIChatClient", chatClient.GetType().Name);
    }

    [Theory]
    [InlineData(LlmProviders.OpenAi)]
    [InlineData(LlmProviders.Anthropic)]
    [InlineData(LlmProviders.Gemini)]
    public void Resolve_ProviderWithoutApiKeyConfigured_Throws(string provider)
    {
        var resolver = CreateResolver();

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(provider, "algum-modelo"));
    }

    [Fact]
    public void Resolve_UnknownProvider_Throws()
    {
        var resolver = CreateResolver(openAiApiKey: "fake-key", anthropicApiKey: "fake-key", geminiApiKey: "fake-key");

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("provedor-desconhecido", "algum-modelo"));
    }

    private static ChatClientResolver CreateResolver(
        string openAiApiKey = "",
        string anthropicApiKey = "",
        string geminiApiKey = "") =>
        new(
            Microsoft.Extensions.Options.Options.Create(new ChatClientOptions { ApiKey = openAiApiKey }),
            Microsoft.Extensions.Options.Options.Create(new AnthropicOptions { ApiKey = anthropicApiKey }),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { ApiKey = geminiApiKey }));
}
