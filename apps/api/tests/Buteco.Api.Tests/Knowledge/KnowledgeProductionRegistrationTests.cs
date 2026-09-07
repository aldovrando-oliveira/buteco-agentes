using Buteco.Api.KnowledgeDocuments.Extraction;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Fecha a lacuna que os testes de unidade da extensão deixam: eles provam que
/// <see cref="KnowledgeExtractorRegistrationExtensions.ValidateKnowledgeExtractorRegistrations"/>
/// funciona, não que o registro **real** de <c>Program.cs</c> é coerente.
///
/// Aqui a coleção capturada é a de produção: o <c>ConfigureServices</c> da
/// <see cref="WebApplicationFactory{TEntryPoint}"/> roda **depois** de todos os
/// registros de <c>Program.cs</c>, então o que chega neste teste é exatamente o
/// que a aplicação compõe.
/// </summary>
public class KnowledgeProductionRegistrationTests
{
    private sealed class ServiceCollectionCapturingFactory : WebApplicationFactory<Program>
    {
        public IServiceCollection? Captured { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services => Captured = services);
        }
    }

    private static IServiceCollection CaptureProductionServices()
    {
        using var factory = new ServiceCollectionCapturingFactory();
        _ = factory.Services;
        return factory.Captured!;
    }

    [Fact]
    public void ProductionRegistration_MatchesTheDeclaredSourceTypes()
    {
        var services = CaptureProductionServices();

        services.ValidateKnowledgeExtractorRegistrations();
    }

    [Fact]
    public void ProductionRegistration_RegistersAnExtractorForEveryDeclaredSourceType()
    {
        var services = CaptureProductionServices();

        var registered = services
            .Where(descriptor => descriptor.ServiceType == typeof(IKnowledgeSourceExtractor) && descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceKey!.ToString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(KnowledgeSourceTypes.All.ToHashSet(StringComparer.Ordinal), registered);
    }
}
