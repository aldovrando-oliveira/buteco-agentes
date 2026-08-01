using Microsoft.Extensions.Configuration;

namespace Buteco.ProviderCatalog;

/// <summary>
/// Identidade de um provedor de LLM e o nome da seção de configuração que ele
/// usa (bind via <c>IConfiguration.GetSection(ConfigSectionName)</c>, mesma
/// convenção de <c>ChatClientOptions</c>/<c>RabbitMqOptions</c>).
/// </summary>
public sealed record LlmProviderDefinition(string Id, string ConfigSectionName);

/// <summary>
/// Único dado que precisa concordar entre <c>apps/api</c> e <c>apps/workers</c>:
/// a identidade de cada provedor e o nome da seção de configuração que ele usa.
/// O catálogo de modelos por provedor não entra aqui — só <c>apps/api</c> o
/// consome (ver design.md da change backend-multi-provedor-llm, Decision 2).
/// </summary>
public static class LlmProviders
{
    public const string OpenAi = "openai";
    public const string Anthropic = "anthropic";
    public const string Gemini = "gemini";

    public static readonly IReadOnlyList<LlmProviderDefinition> All =
    [
        new(OpenAi, "OpenAI"),
        new(Anthropic, "Anthropic"),
        new(Gemini, "Gemini"),
    ];
}

public static class LlmProviderConfigurationExtensions
{
    public static bool IsConfigured(this LlmProviderDefinition provider, IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[$"{provider.ConfigSectionName}:ApiKey"]);
}
