using System.ClientModel;
using Buteco.Workers.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Buteco.Workers.Knowledge.Embedding;

/// <summary>
/// Fábrica do gerador de embedding por provedor.
///
/// <para>
/// <b><c>LlmProviders</c>/<c>ProviderCatalog</c> não serve aqui</b>, e o motivo
/// é concreto: <c>LlmProviders.All</c> declararia <c>anthropic</c> como provedor
/// de embedding, e o assembly <c>Anthropic</c> não tem nenhum tipo de embedding.
/// Aquele catálogo é de provedores de <b>chat</b>; reusá-lo ofereceria ao
/// operador uma opção que estoura ao ser escolhida.
/// </para>
/// </summary>
public sealed class EmbeddingGeneratorResolver(
    IOptions<ChatClientOptions> openAiOptions,
    IOptions<EmbeddingOptions> embeddingOptions) : IEmbeddingGeneratorResolver
{
    public const string OpenAiProvider = "openai";

    public IEmbeddingGenerator<string, Embedding<float>> Resolve()
    {
        var embedding = embeddingOptions.Value;

        if (string.IsNullOrWhiteSpace(embedding.Model))
        {
            throw new InvalidOperationException("Embedding:Model não está configurado.");
        }

        return embedding.Provider switch
        {
            OpenAiProvider => BuildOpenAi(embedding.Model),

            // Mesma exceção que ChatClientResolver lança para provedor
            // desconhecido. `anthropic` e `gemini` caem aqui por AUSÊNCIA DE
            // CAPACIDADE, não por omissão de implementação: o assembly Anthropic
            // referenciado não expõe nenhum tipo de embedding.
            //
            // LEIA ANTES DE ACRESCENTAR UM SEGUNDO PROVEDOR AQUI. Este resolvedor
            // constrói um cliente NOVO a cada chamada, e isso é seguro hoje por um
            // motivo que vale só para o caminho `openai`: `System.ClientModel`
            // serve o transporte de `HttpClientPipelineTransport.Shared`, que tem
            // um `private static readonly HttpClient` — um por processo,
            // independentemente de quantos clientes sejam construídos (verificado
            // por decompilação, ver C4 do design.md da change
            // fix-vazamento-httpclient-chat).
            //
            // Os dois provedores que caem neste braço NÃO têm essa propriedade no
            // lado de chat: `Google.GenAI` faz `new HttpClient()` por instância de
            // client, e o SDK do Anthropic também. Foi exatamente esse padrão que
            // vazou ~44 descritores por mensagem e obrigou o cache por
            // (provider, model) em `ChatClientResolver`.
            //
            // E aqui a frequência é PIOR que a daquele achado: desde a etapa 4 da
            // linha de conhecimento, a consulta gera um embedding por MENSAGEM de
            // agente, não um por documento indexado. O vazamento que lá foi
            // descoberto na frequência de indexação nasceria aqui ordens de
            // grandeza acima dela, e no caminho quente.
            //
            // Portanto: um provedor de embedding não-OpenAI precisa vir com cache
            // por (provider, model), no molde de `ChatClientResolver` — não depois,
            // quando o perfilamento acusar. O perfilamento já acusou, uma vez, no
            // lado de chat.
            _ => throw new InvalidOperationException(
                $"Provedor de embedding '{embedding.Provider}' não é suportado."),
        };
    }

    private IEmbeddingGenerator<string, Embedding<float>> BuildOpenAi(string model)
    {
        var options = openAiOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Provedor de embedding 'openai' não está configurado (OpenAI:ApiKey ausente).");
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl) });

        // SEM o segundo parâmetro (defaultModelDimensions), de propósito: ele
        // preenche a metadata do gerador com uma dimensão que o gateway NÃO
        // garante — foi medido que ele aceita `dimensions` e o ignora. Declarar
        // ali produziria metadata mentindo sobre o vetor real.
        //
        // A conferência de dimensão acontece no caminho de ESCRITA, contra o
        // que a resposta trouxe, que é o único lugar onde a dimensão real é
        // conhecida.
        return client.GetEmbeddingClient(model).AsIEmbeddingGenerator();
    }
}
