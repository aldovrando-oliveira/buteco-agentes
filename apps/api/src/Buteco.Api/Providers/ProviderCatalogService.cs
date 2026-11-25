using Buteco.Api.Infrastructure;
using Buteco.Api.Providers.Responses;
using Buteco.ProviderCatalog;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Providers;

/// <summary>
/// Único ponto de <c>apps/api</c> que combina a disponibilidade de ambiente
/// (<see cref="LlmProviders"/>/<c>IsConfigured</c>) com a lista curada de modelos
/// habilitados (<c>llm_enabled_models</c>). Usado por <c>GET /providers</c>, pelos
/// handlers de Create/Update de agente, por <c>EnqueueingAgentHandler</c> (que o
/// resolve do próprio escopo) e por <c>GET /providers/models</c> (design.md da change
/// modelos-curados-por-provedor, D2 e D7).
///
/// <para>
/// <b>Scoped, porque lê o banco.</b> Antes desta change era singleton sem banco, com o
/// catálogo estático <c>ProviderModelCatalog</c>, removido nesta change (convenção 25:
/// o último consumidor saiu, a declaração sai).
/// </para>
///
/// <para>
/// <b>A validação de agente e a recusa do <c>SendMessage</c> são o mesmo método</b>
/// (<see cref="ValidateAsync"/>): a regra "par disponível" e a ordem "chave antes de
/// modelo" moram aqui, e só aqui. O teste que amarra os dois lados é o de
/// <c>ProviderAvailabilityTests.AgentValidation_AndSendMessage_AgreeOnThePair</c>.
/// </para>
/// </summary>
public sealed class ProviderCatalogService(IConfiguration configuration, AppDbContext dbContext)
{
    /// <summary>
    /// Os provedores configurados, na ordem de <see cref="LlmProviders.All"/>, cada um com
    /// os modelos habilitados. Provedor configurado sem modelo habilitado aparece com a
    /// lista vazia: a chave existe e ninguém habilitou nada, o que é diferente de
    /// provedor sem chave, que não aparece (D2, convenção 13).
    /// </summary>
    public async Task<IReadOnlyList<ProviderResponse>> GetAvailableProvidersAsync(CancellationToken cancellationToken)
    {
        var enabled = await GetEnabledModelsAsync(cancellationToken);

        return LlmProviders.All
            .Where(provider => provider.IsConfigured(configuration))
            .Select(provider => new ProviderResponse(provider.Id, ModelsOf(enabled, provider.Id)))
            .ToList();
    }

    public async Task<ProviderValidationOutcome> ValidateAsync(string provider, string model, CancellationToken cancellationToken)
    {
        var definition = LlmProviders.All.FirstOrDefault(p => p.Id == provider);
        if (definition is null || !definition.IsConfigured(configuration))
        {
            return ProviderValidationOutcome.ProviderNotConfigured;
        }

        var isEnabled = await dbContext.EnabledLlmModels
            .AsNoTracking()
            .AnyAsync(enabledModel => enabledModel.Provider == definition.Id && enabledModel.Model == model, cancellationToken);

        return isEnabled ? ProviderValidationOutcome.Valid : ProviderValidationOutcome.ModelUnavailable;
    }

    /// <summary>
    /// A lista curada inteira, na ordem do banco: por provedor e, dentro dele, por
    /// <c>Model</c>. O par é a chave primária, então o <c>Model</c> já é desempate total
    /// dentro do provedor (<c>api-response-ordering</c>). É a consulta única que
    /// <c>GET /providers</c> e <c>GET /providers/models</c> usam: nenhuma das duas
    /// reordena em memória, porque a collation do banco e o comparador do .NET discordam
    /// (convenção 6).
    /// </summary>
    public async Task<IReadOnlyList<(string Provider, string Model)>> GetEnabledModelsAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.EnabledLlmModels
            .AsNoTracking()
            .OrderBy(enabledModel => enabledModel.Provider)
            .ThenBy(enabledModel => enabledModel.Model)
            .Select(enabledModel => new { enabledModel.Provider, enabledModel.Model })
            .ToListAsync(cancellationToken);

        return rows.Select(row => (row.Provider, row.Model)).ToList();
    }

    /// <summary>
    /// Se a chave do provedor está no ambiente. Desde esta change o <c>EnqueueingAgentHandler</c>
    /// não o usa mais (usa <see cref="ValidateAsync"/>); os consumidores são as rotas de
    /// curadoria (o <c>configured</c> de <c>GET /providers/models</c> e o <c>409</c> de
    /// habilitar, D6).
    /// </summary>
    public bool IsProviderConfigured(string provider) =>
        LlmProviders.All.FirstOrDefault(p => p.Id == provider) is { } definition && definition.IsConfigured(configuration);

    /// <summary>Filtra preservando a ordem do banco (o <c>Where</c> do LINQ é estável).</summary>
    public static IReadOnlyList<string> ModelsOf(IReadOnlyList<(string Provider, string Model)> enabled, string provider) =>
        enabled.Where(pair => pair.Provider == provider).Select(pair => pair.Model).ToList();
}
