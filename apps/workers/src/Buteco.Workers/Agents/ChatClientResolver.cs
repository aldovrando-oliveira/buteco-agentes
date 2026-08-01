using System.ClientModel;
using Anthropic;
using Buteco.ProviderCatalog;
using Buteco.Workers.Options;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Buteco.Workers.Agents;

/// <summary>
/// Fábrica de <see cref="IChatClient"/> por provedor (Decision 7 do
/// design.md da change backend-multi-provedor-llm). Lança
/// <see cref="InvalidOperationException"/> para provedor desconhecido ou com
/// configuração ausente — capturado pelo <c>catch (Exception)</c> já
/// existente em <see cref="AgentExecutionService.ExecuteAsync"/>, rede de
/// segurança para divergência de ambiente entre <c>apps/api</c> e
/// <c>apps/workers</c> (Decision 5).
/// </summary>
public sealed class ChatClientResolver(
    IOptions<ChatClientOptions> openAiOptions,
    IOptions<AnthropicOptions> anthropicOptions,
    IOptions<GeminiOptions> geminiOptions) : IChatClientResolver
{
    public IChatClient Resolve(string provider, string model) => provider switch
    {
        LlmProviders.OpenAi => BuildOpenAi(model),
        LlmProviders.Anthropic => BuildAnthropic(model),
        LlmProviders.Gemini => BuildGemini(model),
        _ => throw new InvalidOperationException($"Provedor '{provider}' não é suportado."),
    };

    private IChatClient BuildOpenAi(string model)
    {
        var options = openAiOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("Provedor 'openai' não está configurado (ChatClient:ApiKey ausente).");
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
