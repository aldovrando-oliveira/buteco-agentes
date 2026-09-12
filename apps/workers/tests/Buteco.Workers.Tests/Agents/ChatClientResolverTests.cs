using Buteco.ProviderCatalog;
using Buteco.Workers.Agents;
using Buteco.Workers.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

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

        Assert.Equal("OpenAIChatClient", InnerTypeName(chatClient));
    }

    [Fact]
    public void Resolve_Anthropic_BuildsAnthropicChatClient()
    {
        var resolver = CreateResolver(anthropicApiKey: "fake-key");

        var chatClient = resolver.Resolve(LlmProviders.Anthropic, "claude-opus-5");

        Assert.Equal("AnthropicChatClient", InnerTypeName(chatClient));
    }

    [Fact]
    public void Resolve_Gemini_BuildsGoogleGenAIChatClient()
    {
        var resolver = CreateResolver(geminiApiKey: "fake-key");

        var chatClient = resolver.Resolve(LlmProviders.Gemini, "gemini-3.6-flash");

        Assert.Equal("GoogleGenAIChatClient", InnerTypeName(chatClient));
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

    /// <summary>
    /// GUARDA DO DEFEITO REAL (convenção 15): verificado reprovando contra a
    /// criação por mensagem, que vazava um pool de conexões HTTP por mensagem
    /// processada (~44 descritores, sem retorno). Afirma a PROPRIEDADE — mesma
    /// referência para a mesma chave —, não o sintoma, porque o defeito é de
    /// ciclo de vida e degrada com o tempo de processo, não numa chamada isolada.
    /// </summary>
    [Fact]
    public void Resolve_SameProviderAndModelTwice_ReturnsSameInstance()
    {
        var resolver = CreateResolver(openAiApiKey: "fake-key");

        var first = resolver.Resolve(LlmProviders.OpenAi, "gpt-5.6-sol");
        var second = resolver.Resolve(LlmProviders.OpenAi, "gpt-5.6-sol");

        Assert.Same(first, second);
    }

    /// <summary>
    /// Par da convenção 5 do guarda acima. NÃO reprova contra o defeito
    /// corrigido por esta change — passa com a criação por mensagem também, e
    /// isso está registrado de propósito: ele existe para fechar um defeito
    /// FUTURO, um cache chaveado só por `provider`, que devolveria o client de
    /// um modelo no lugar do outro.
    /// </summary>
    [Fact]
    public void Resolve_SameProviderDifferentModel_ReturnsDifferentInstances()
    {
        var resolver = CreateResolver(openAiApiKey: "fake-key");

        var first = resolver.Resolve(LlmProviders.OpenAi, "gpt-5.6-sol");
        var second = resolver.Resolve(LlmProviders.OpenAi, "gpt-5.6-mini");

        Assert.NotSame(first, second);
    }

    /// <summary>
    /// Mesma natureza do teste acima: não reprova contra o defeito atual,
    /// existe para o defeito futuro de colapsar provedores distintos na mesma
    /// entrada de cache.
    /// </summary>
    [Fact]
    public void Resolve_DifferentProviders_ReturnsDifferentInstances()
    {
        var resolver = CreateResolver(openAiApiKey: "fake-key", anthropicApiKey: "fake-key");

        var openAi = resolver.Resolve(LlmProviders.OpenAi, "gpt-5.6-sol");
        var anthropic = resolver.Resolve(LlmProviders.Anthropic, "claude-opus-5");

        Assert.NotSame(openAi, anthropic);
    }

    /// <summary>
    /// Guarda de "falha não memorizada" (R3 do design.md). NÃO reprova contra o
    /// defeito atual — passa hoje, porque hoje não há cache nenhum. Existe para
    /// reprovar contra uma implementação de cache com <c>Lazy&lt;T&gt;</c> (que
    /// memoriza a exceção e transformaria credencial ausente em falha
    /// permanente do processo) ou com <c>GetOrAdd</c> sobre fábrica que lança.
    ///
    /// <para>
    /// O teste já existente <c>Resolve_ProviderWithoutApiKeyConfigured_Throws</c>
    /// resolve UMA vez e passaria com esse defeito presente — é a distinção que
    /// justifica este segundo teste em vez de uma asserção a mais lá.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(LlmProviders.OpenAi)]
    [InlineData(LlmProviders.Anthropic)]
    [InlineData(LlmProviders.Gemini)]
    public void Resolve_ProviderWithoutApiKeyConfigured_ThrowsOnEveryCall(string provider)
    {
        var resolver = CreateResolver();

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(provider, "algum-modelo"));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(provider, "algum-modelo"));
    }

    private static ChatClientResolver CreateResolver(
        string openAiApiKey = "",
        string anthropicApiKey = "",
        string geminiApiKey = "") =>
        new(
            Microsoft.Extensions.Options.Options.Create(new ChatClientOptions { ApiKey = openAiApiKey }),
            Microsoft.Extensions.Options.Options.Create(new AnthropicOptions { ApiKey = anthropicApiKey }),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { ApiKey = geminiApiKey }),
            NullLoggerFactory.Instance);

    /// <summary>
    /// O resolver devolve o wrapper de duração, e o client do SDK fica dentro
    /// dele — por isso a asserção de "qual tipo para qual provedor" desce um
    /// nível em vez de ler o tipo devolvido, que é o mesmo para os três.
    /// </summary>
    private static string InnerTypeName(IChatClient chatClient) =>
        Assert.IsType<LlmCallDurationChatClient>(chatClient).InnerChatClient.GetType().Name;
}
