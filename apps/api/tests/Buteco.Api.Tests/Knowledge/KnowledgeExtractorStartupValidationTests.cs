using Buteco.Api.KnowledgeDocuments.Extraction;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Testa <see cref="KnowledgeExtractorRegistrationExtensions.ValidateKnowledgeExtractorRegistrations"/>
/// diretamente sobre uma <see cref="IServiceCollection"/> — que é exatamente
/// como <c>Program.cs</c> a chama.
///
/// **Por que não por fixture de host** (divergência do desenho original,
/// registrada em design.md, D12): a checagem inspeciona descritores de DI
/// keyed e roda sobre <c>builder.Services</c> **antes** de <c>Build()</c>.
/// Uma <c>WebApplicationFactory</c> só consegue injetar serviços depois que
/// <c>Program.cs</c> registrou os seus e já rodou o validador — então nenhuma
/// das duas divergências seria observável por aquele caminho. Este teste
/// exercita o mesmo código, no mesmo ponto do ciclo.
/// </summary>
public class KnowledgeExtractorStartupValidationTests
{
    private sealed class UnusedExtractor : IKnowledgeSourceExtractor
    {
        public ExtractionResult Extract(string rawContent) => ExtractionResult.Success(rawContent);
    }

    [Fact]
    public void Validate_WithEveryDeclaredTypeRegistered_Succeeds()
    {
        var services = new ServiceCollection();
        foreach (var sourceType in KnowledgeSourceTypes.All)
        {
            services.AddKeyedSingleton<IKnowledgeSourceExtractor, UnusedExtractor>(sourceType);
        }

        services.ValidateKnowledgeExtractorRegistrations();
    }

    // Sentido 1: declarado sem extrator. Sem esta checagem, o cadastro
    // aceitaria o sourceType na validação e estouraria ao resolver o extrator,
    // já dentro do handler.
    [Fact]
    public void Validate_WithDeclaredTypeMissingItsExtractor_FailsIdentifyingTheType()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.ValidateKnowledgeExtractorRegistrations());

        Assert.Contains(KnowledgeSourceTypes.Markdown, exception.Message);
        Assert.Contains("não tem", exception.Message);
    }

    // Sentido 2 — o que não é óbvio, e o motivo de a convenção 8 exigir a
    // checagem nos dois sentidos: um extrator registrado para um valor não
    // declarado fica alcançável pela API sem nunca ter sido declarado como
    // suportado, e nenhuma listagem de tipos suportados o mostraria.
    [Fact]
    public void Validate_WithExtractorRegisteredForUndeclaredType_FailsIdentifyingTheValue()
    {
        var services = new ServiceCollection();
        foreach (var sourceType in KnowledgeSourceTypes.All)
        {
            services.AddKeyedSingleton<IKnowledgeSourceExtractor, UnusedExtractor>(sourceType);
        }

        services.AddKeyedSingleton<IKnowledgeSourceExtractor, UnusedExtractor>("pdf");

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.ValidateKnowledgeExtractorRegistrations());

        Assert.Contains("pdf", exception.Message);
        Assert.Contains("não está declarado", exception.Message);
    }

    // O registro real de Program.cs precisa passar: é o que garante que a
    // aplicação sobe.
    [Fact]
    public void Validate_WithTheRealProductionRegistration_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IKnowledgeSourceExtractor, MarkdownSourceExtractor>(KnowledgeSourceTypes.Markdown);

        services.ValidateKnowledgeExtractorRegistrations();
    }
}
