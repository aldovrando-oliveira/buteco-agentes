using Buteco.ProviderCatalog;

namespace Buteco.Api.Providers.Vendors;

public static class VendorModelListerRegistrationExtensions
{
    /// <summary>
    /// Os três listers, keyed pelo id do provedor, e um <c>HttpClient</c> nomeado por
    /// fornecedor com 15 s por página (design.md da change modelos-curados-por-provedor, D4).
    /// </summary>
    public static IServiceCollection AddVendorModelListers(this IServiceCollection services)
    {
        foreach (var provider in LlmProviders.All)
        {
            services.AddHttpClient(VendorJson.HttpClientName(provider.Id), client => client.Timeout = VendorJson.PageTimeout);
        }

        services.AddKeyedSingleton<IVendorModelLister, OpenAiModelLister>(LlmProviders.OpenAi);
        services.AddKeyedSingleton<IVendorModelLister, AnthropicModelLister>(LlmProviders.Anthropic);
        services.AddKeyedSingleton<IVendorModelLister, GeminiModelLister>(LlmProviders.Gemini);
        services.AddSingleton<VendorModelCatalog>();

        return services;
    }

    /// <summary>
    /// Checagem de boot dos listers, nos dois sentidos (convenção 8; mesmo molde de
    /// <c>ValidateKnowledgeExtractorRegistrations</c>), sobre a <see cref="IServiceCollection"/>,
    /// antes do <c>Build()</c>, porque é ali que as chaves registradas são enumeráveis:
    ///
    /// - provedor de <see cref="LlmProviders.All"/> sem lister faria a rota de opções aceitar o
    ///   id e estourar ao resolver o lister;
    /// - lister registrado para um id fora da lista ficaria alcançável sem o provedor existir.
    ///
    /// Sem bypass: lança e o processo não sobe. O teste sobre a composição real do
    /// <c>Program.cs</c> é o que pega a chamada removida (convenção 8, forma
    /// <c>IServiceCollection</c>).
    /// </summary>
    public static void ValidateVendorModelListerRegistrations(this IServiceCollection services)
    {
        var declared = LlmProviders.All.Select(provider => provider.Id).ToHashSet(StringComparer.Ordinal);

        var registered = services
            .Where(descriptor => descriptor.ServiceType == typeof(IVendorModelLister) && descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceKey!.ToString()!)
            .ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();

        problems.AddRange(declared
            .Except(registered)
            .Order(StringComparer.Ordinal)
            .Select(id => $"'{id}' está em {nameof(LlmProviders)}.{nameof(LlmProviders.All)} mas não tem {nameof(IVendorModelLister)} registrado"));

        problems.AddRange(registered
            .Except(declared)
            .Order(StringComparer.Ordinal)
            .Select(id => $"'{id}' tem {nameof(IVendorModelLister)} registrado mas não está em {nameof(LlmProviders)}.{nameof(LlmProviders.All)}"));

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Registro das listagens de modelos dos fornecedores incompleto: {string.Join("; ", problems)}.");
        }
    }
}
