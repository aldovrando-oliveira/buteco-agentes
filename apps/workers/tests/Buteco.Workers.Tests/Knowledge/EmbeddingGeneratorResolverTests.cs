using Buteco.Workers.Knowledge.Embedding;
using Buteco.Workers.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// Molde de <c>ChatClientResolverTests</c>: afirma qual gerador é construído
/// para qual provedor, sem nenhuma chamada de rede.
/// </summary>
public class EmbeddingGeneratorResolverTests
{
    [Fact]
    public void ConfiguredOpenAiProvider_BuildsAGenerator()
    {
        var resolver = Build("openai", "modelo-x", apiKey: "chave");

        Assert.NotNull(resolver.Resolve());
    }

    [Fact]
    public void OpenAiWithoutApiKey_Throws()
    {
        var resolver = Build("openai", "modelo-x", apiKey: string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(resolver.Resolve);
        Assert.Contains("não está configurado", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingModel_Throws()
    {
        var resolver = Build("openai", string.Empty, apiKey: "chave");

        var exception = Assert.Throws<InvalidOperationException>(resolver.Resolve);
        Assert.Contains("Embedding:Model", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>anthropic</c> e <c>gemini</c> caem na mesma exceção de "não suportado"
    /// que <c>ChatClientResolver</c> usa para provedor desconhecido — e o motivo
    /// é <b>ausência de capacidade</b>, não omissão de implementação: o assembly
    /// <c>Anthropic</c> referenciado não expõe nenhum tipo de embedding.
    ///
    /// <para>
    /// É por isso que o catálogo de provedores de chat (<c>LlmProviders</c>) não
    /// serve aqui: reusá-lo ofereceria ao operador uma opção que estoura ao ser
    /// escolhida.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("anthropic")]
    [InlineData("gemini")]
    [InlineData("provedor-inventado")]
    public void ProvidersWithoutEmbeddingCapability_Throw(string provider)
    {
        var resolver = Build(provider, "modelo-x", apiKey: "chave");

        var exception = Assert.Throws<InvalidOperationException>(resolver.Resolve);
        Assert.Contains("não é suportado", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(provider, exception.Message, StringComparison.Ordinal);
    }

    private static EmbeddingGeneratorResolver Build(string provider, string model, string apiKey) =>
        new(MsOptions.Create(new ChatClientOptions { ApiKey = apiKey, BaseUrl = "https://exemplo.invalido/v1" }),
            MsOptions.Create(new EmbeddingOptions { Provider = provider, Model = model, Dimensions = 4096 }));
}
