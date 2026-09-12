using System.ClientModel;
using Anthropic;
using Buteco.ProviderCatalog;
using Buteco.Workers.Options;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Buteco.Workers.Agents;

/// <summary>
/// Resolve o <see cref="IChatClient"/> por provedor, mantendo UMA instância viva
/// por par <c>(provider, model)</c> durante toda a vida do processo.
///
/// <para>
/// <b>Por que cache, e não construção por chamada.</b> A change
/// <c>backend-multi-provedor-llm</c> decidiu construir por chamada como Non-Goal
/// explícito, registrando o gatilho: <i>"simples de trocar por cache depois, se
/// perfilamento mostrar necessidade"</i> (Decision 7). O perfilamento mostrou —
/// ~44 descritores de arquivo vazados por mensagem processada, sem retorno, até
/// o worker parar de responder. Os SDKs de Gemini e de Anthropic instanciam um
/// <c>HttpClient</c> próprio por client (o da OpenAI não: usa
/// <c>HttpClientPipelineTransport.Shared</c>, estático — e é por isso que só o
/// chat degradava), então cada mensagem vazava um pool de conexões inteiro.
/// Esta classe dispara aquele gatilho; não contraria aquela decisão.
/// </para>
///
/// <para>
/// <b>É o que o contrato do tipo pede.</b> A documentação de
/// <see cref="IChatClient"/> diz que implementações devem suportar uso
/// concorrente por múltiplas requisições e que instâncias não devem ser
/// descartadas enquanto em uso. Reuso sempre foi o desenho.
/// </para>
/// </summary>
public sealed class ChatClientResolver(
    IOptions<ChatClientOptions> openAiOptions,
    IOptions<AnthropicOptions> anthropicOptions,
    IOptions<GeminiOptions> geminiOptions,
    ILoggerFactory loggerFactory) : IChatClientResolver
{
    // A CHAVE É (provider, model), e basta porque A CREDENCIAL É POR PROCESSO,
    // NÃO POR AGENTE: a entidade Agent tem Provider e Model e nenhum campo de
    // credencial; as três Options vêm de configuração do processo
    // (Program.cs:18-20), na prática de variável de ambiente. Dois agentes com o
    // mesmo (provider, model) usam necessariamente a mesma credencial.
    //
    // NO DIA EM QUE EXISTIR CREDENCIAL POR AGENTE, A CHAVE TEM QUE MUDAR.
    // Sem este registro isso passa despercebido, e o efeito seria um agente
    // usando a credencial de outro.
    //
    // NUNCA DESALOJA, de propósito: a cardinalidade é limitada pelos pares
    // realmente cadastrados, e nada torna uma entrada obsoleta enquanto o
    // processo vive. Se algum dia entrar evicção (IMemoryCache ou equivalente),
    // O ITEM EVICTADO PRECISA SER DESCARTADO — senão o vazamento que esta classe
    // corrige volta mais devagar, que é pior de diagnosticar que o original.
    private readonly Dictionary<(string Provider, string Model), IChatClient> _clients = [];

    private readonly Lock _gate = new();

    public IChatClient Resolve(string provider, string model)
    {
        var key = (provider, model);

        // Leitura sob o mesmo lock da escrita. O custo é um lock por mensagem,
        // na casa dos microssegundos, contra uma chamada de rede a um LLM —
        // não há trade-off a discutir aqui.
        //
        // NÃO USAR Lazy<T> NEM ConcurrentDictionary.GetOrAdd. As duas são as
        // escolhas óbvias para "cache concorrente em C#", as duas parecem mais
        // simples que isto, e as duas estão erradas por motivo específico deste
        // caso:
        //
        //   - Lazy<T> (ExecutionAndPublication) MEMORIZA A EXCEÇÃO e a re-lança
        //     para sempre. Build* lança InvalidOperationException quando a
        //     credencial do provedor está ausente, então uma configuração
        //     corrigida viraria falha permanente até reiniciar o processo. Esse
        //     caminho é exercitado de verdade por
        //     TaskJobConsumerTests.Consumer_WhenAgentProviderNotConfiguredInWorkerEnvironment_TaskEndsFailed,
        //     que usa este resolver real, e pelo guarda
        //     ChatClientResolverTests.Resolve_ProviderWithoutApiKeyConfigured_ThrowsOnEveryCall.
        //
        //   - GetOrAdd pode EXECUTAR A FÁBRICA MAIS DE UMA VEZ sob concorrência
        //     e descartar o perdedor. O perdedor aqui não é um objeto barato: é
        //     um client com pool de conexões próprio — ou seja, exatamente o
        //     defeito que esta classe corrige, reintroduzido em escala menor.
        //
        // Com o lock, a construção que lança propaga e NADA é gravado, então a
        // resolução seguinte tenta de novo.
        lock (_gate)
        {
            if (_clients.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var client = Build(provider, model);
            _clients[key] = client;
            return client;
        }
    }

    // O wrapper de duração é composto AQUI, uma vez por entrada do cache, e não
    // por mensagem. Duas razões, as duas verificadas:
    //
    //   - POSIÇÃO: ChatClientAgent empilha o middleware dele (incluindo
    //     FunctionInvokingChatClient) POR FORA do client recebido, via
    //     WithDefaultAgentMiddleware. Composto aqui, o wrapper fica na camada
    //     mais interna e mede cada requisição HTTP ao provedor, SEM o tempo de
    //     execução das tools — que é justamente a distinção que faltava quando
    //     este vazamento foi diagnosticado.
    //
    //   - CICLO DE VIDA: LlmCallDurationChatClient é um DelegatingChatClient, e
    //     DelegatingChatClient.Dispose() descarta o InnerClient em cascata. Se
    //     ele fosse construído e descartado por mensagem, levaria o client
    //     cacheado junto.
    private IChatClient Build(string provider, string model)
    {
        var inner = provider switch
        {
            LlmProviders.OpenAi => BuildOpenAi(model),
            LlmProviders.Anthropic => BuildAnthropic(model),
            LlmProviders.Gemini => BuildGemini(model),
            _ => throw new InvalidOperationException($"Provedor '{provider}' não é suportado."),
        };

        return new LlmCallDurationChatClient(
            inner,
            provider,
            model,
            loggerFactory.CreateLogger<LlmCallDurationChatClient>());
    }

    private IChatClient BuildOpenAi(string model)
    {
        var options = openAiOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("Provedor 'openai' não está configurado (OpenAI:ApiKey ausente).");
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl) });

        return client.GetChatClient(model).AsIChatClient();
    }

    private IChatClient BuildAnthropic(string model)
    {
        var options = anthropicOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("Provedor 'anthropic' não está configurado (Anthropic:ApiKey ausente).");
        }

        AnthropicClient client = new() { ApiKey = options.ApiKey };
        return client.AsIChatClient(model);
    }

    private IChatClient BuildGemini(string model)
    {
        var options = geminiOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("Provedor 'gemini' não está configurado (Gemini:ApiKey ausente).");
        }

        var client = new Client(apiKey: options.ApiKey);
        return client.AsIChatClient(model);
    }
}
