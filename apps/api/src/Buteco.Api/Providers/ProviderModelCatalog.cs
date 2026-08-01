using Buteco.ProviderCatalog;

namespace Buteco.Api.Providers;

/// <summary>
/// Catálogo estático de modelos por provedor (Decision 3 do design.md da
/// change backend-multi-provedor-llm) — sem consulta dinâmica às APIs de
/// listagem de modelo de cada fornecedor. Levantado contra o estado real de
/// cada fornecedor em julho de 2026; fica desatualizado conforme novos
/// modelos são lançados (risco aceito, não Non-Goal).
/// </summary>
public static class ProviderModelCatalog
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ModelsByProvider =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [LlmProviders.OpenAi] = ["gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna"],
            [LlmProviders.Anthropic] = ["claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5"],
            [LlmProviders.Gemini] = ["gemini-3.6-flash", "gemini-3.5-flash", "gemini-3.1-flash-lite"],
        };
}
