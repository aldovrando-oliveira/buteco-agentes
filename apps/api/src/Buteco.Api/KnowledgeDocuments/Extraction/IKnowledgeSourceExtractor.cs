namespace Buteco.Api.KnowledgeDocuments.Extraction;

/// <summary>
/// Contrato de extração por <c>SourceType</c>, resolvido via DI **keyed**
/// (<c>AddKeyedSingleton&lt;IKnowledgeSourceExtractor&gt;(sourceType)</c>) —
/// mesmo idioma dos contratos de adapter de canal de <c>apps/inbox</c>
/// (design.md, D12).
///
/// A completude do registro é garantida por
/// <see cref="KnowledgeExtractorRegistrationExtensions.ValidateKnowledgeExtractorRegistrations"/>
/// no startup, nos dois sentidos (convenção 8).
/// </summary>
public interface IKnowledgeSourceExtractor
{
    /// <param name="rawContent">
    /// Conteúdo como o cliente o enviou. Para os tipos suportados nesta etapa é
    /// sempre texto — multipart e tipos binários nascem com o primeiro extrator
    /// que precise dos bytes (design.md, D3).
    /// </param>
    ExtractionResult Extract(string rawContent);
}
