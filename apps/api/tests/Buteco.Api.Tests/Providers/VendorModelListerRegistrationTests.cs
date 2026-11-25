using Buteco.Api.Providers.Vendors;
using Buteco.ProviderCatalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Providers;

/// <summary>
/// A checagem de boot das listagens dos fornecedores (design.md da change
/// modelos-curados-por-provedor, D4; convenção 8, forma <see cref="IServiceCollection"/>): a
/// extensão nos dois sentidos, e a composição REAL do <c>Program.cs</c>, capturada pelo
/// <c>ConfigureServices</c> da factory, que roda depois de todos os registros. O segundo é o
/// que pega "chamada removida e registro divergente", que os testes da extensão não veem.
/// </summary>
public class VendorModelListerRegistrationTests
{
    [Fact]
    public void AProviderWithoutLister_FailsTheBoot_NamingIt()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IVendorModelLister, OpenAiModelLister>(LlmProviders.OpenAi);
        services.AddKeyedSingleton<IVendorModelLister, AnthropicModelLister>(LlmProviders.Anthropic);

        var exception = Assert.Throws<InvalidOperationException>(services.ValidateVendorModelListerRegistrations);

        Assert.Contains("'gemini' está em LlmProviders.All mas não tem IVendorModelLister registrado", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AListerOutsideTheProviderList_FailsTheBoot_NamingIt()
    {
        var services = new ServiceCollection();
        services.AddVendorModelListers();
        services.AddKeyedSingleton<IVendorModelLister, OpenAiModelLister>("mistral");

        var exception = Assert.Throws<InvalidOperationException>(services.ValidateVendorModelListerRegistrations);

        Assert.Contains("'mistral' tem IVendorModelLister registrado mas não está em LlmProviders.All", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExtension_RegistersOneListerPerProvider()
    {
        var services = new ServiceCollection();
        services.AddVendorModelListers();

        services.ValidateVendorModelListerRegistrations();
    }

    [Fact]
    public void ProductionComposition_HasExactlyOneListerPerProvider()
    {
        using var factory = new ServiceCollectionCapturingFactory();
        _ = factory.Services;
        var services = factory.Captured!;

        services.ValidateVendorModelListerRegistrations();
        var registered = services
            .Where(descriptor => descriptor.ServiceType == typeof(IVendorModelLister) && descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceKey!.ToString()!)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(LlmProviders.All.Select(provider => provider.Id).Order(StringComparer.Ordinal), registered);
    }

    private sealed class ServiceCollectionCapturingFactory : WebApplicationFactory<Program>
    {
        public IServiceCollection? Captured { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Mesmo motivo de KnowledgeProductionRegistrationTests: o boot confere TZ.
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["TZ"] = TimeZoneInfo.Local.Id }));

            builder.ConfigureServices(services => Captured = services);
        }
    }
}
