using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.KnowledgeDocuments.Extraction;

public static class KnowledgeExtractorRegistrationExtensions
{
    /// <summary>
    /// Checagem de integridade do startup para os extratores registrados por DI
    /// keyed — mesmo molde de
    /// <c>ValidateChannelAdapterRegistrations</c> (apps/inbox) e de
    /// <c>ValidateTimeZoneConfiguration</c> (apps/workers). Convenção 8:
    /// checagem no boot é o padrão para todo registro que possa ficar
    /// incompleto em silêncio, não documentação em prosa.
    ///
    /// **Vale nos dois sentidos**, e o segundo é o que não é óbvio:
    ///
    /// - tipo declarado em <see cref="KnowledgeSourceTypes.All"/> sem extrator
    ///   registrado faria o cadastro aceitar o `sourceType` na validação e
    ///   estourar ao resolver o extrator, já dentro do handler;
    /// - extrator registrado para um valor **não declarado** ficaria
    ///   alcançável pela API sem nunca ter sido declarado como suportado — um
    ///   tipo entrando em produção por descuido de registro, que nenhuma
    ///   listagem de tipos suportados mostraria.
    ///
    /// Sem bypass e sem modo de tolerância: lança e o processo não sobe.
    /// </summary>
    public static void ValidateKnowledgeExtractorRegistrations(this IServiceCollection services)
    {
        var declared = KnowledgeSourceTypes.All.ToHashSet(StringComparer.Ordinal);

        var registered = services
            .Where(descriptor => descriptor.ServiceType == typeof(IKnowledgeSourceExtractor) && descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceKey!.ToString()!)
            .ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();

        problems.AddRange(declared
            .Except(registered)
            .Order(StringComparer.Ordinal)
            .Select(sourceType => $"'{sourceType}' está declarado em {nameof(KnowledgeSourceTypes)}.{nameof(KnowledgeSourceTypes.All)} mas não tem {nameof(IKnowledgeSourceExtractor)} registrado"));

        problems.AddRange(registered
            .Except(declared)
            .Order(StringComparer.Ordinal)
            .Select(sourceType => $"'{sourceType}' tem {nameof(IKnowledgeSourceExtractor)} registrado mas não está declarado em {nameof(KnowledgeSourceTypes)}.{nameof(KnowledgeSourceTypes.All)}"));

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Registro de extratores de conhecimento incompleto: {string.Join("; ", problems)}.");
        }
    }
}
