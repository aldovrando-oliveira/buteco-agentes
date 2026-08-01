using Buteco.Api.Providers.Responses;
using Buteco.ProviderCatalog;

namespace Buteco.Api.Providers;

/// <summary>
/// Único ponto de <c>apps/api</c> que combina o catálogo estático de modelos
/// (<see cref="ProviderModelCatalog"/>) com a disponibilidade de ambiente
/// (<see cref="LlmProviders"/>/<c>IsConfigured</c>) — usado por
/// <c>GET /providers</c>, pelos handlers de Create/Update de agente e por
/// <c>EnqueueingAgentHandler</c> (ver Decisions 3, 4 e 5 do design.md da
/// change backend-multi-provedor-llm). Sem dependência de banco, seguro como
/// singleton.
/// </summary>
public sealed class ProviderCatalogService(IConfiguration configuration)
{
    public IReadOnlyList<ProviderResponse> GetAvailableProviders() =>
        LlmProviders.All
            .Where(provider => provider.IsConfigured(configuration))
            .Select(provider => new ProviderResponse(provider.Id, ProviderModelCatalog.ModelsByProvider[provider.Id]))
            .ToList();

    public ProviderValidationOutcome Validate(string provider, string model)
    {
        var definition = LlmProviders.All.FirstOrDefault(p => p.Id == provider);
        if (definition is null || !definition.IsConfigured(configuration))
        {
            return ProviderValidationOutcome.ProviderNotConfigured;
        }

        return ProviderModelCatalog.ModelsByProvider[definition.Id].Contains(model)
            ? ProviderValidationOutcome.Valid
            : ProviderValidationOutcome.ModelUnavailable;
    }

    public bool IsProviderConfigured(string provider)
    {
        var definition = LlmProviders.All.FirstOrDefault(p => p.Id == provider);
        return definition is not null && definition.IsConfigured(configuration);
    }
}
