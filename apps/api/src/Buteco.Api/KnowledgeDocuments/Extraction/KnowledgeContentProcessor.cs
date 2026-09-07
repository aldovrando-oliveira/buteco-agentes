using Buteco.Api.KnowledgeDocuments.Options;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace Buteco.Api.KnowledgeDocuments.Extraction;

/// <summary>
/// Resolve o extrator do <c>sourceType</c>, extrai e aplica o teto de tamanho.
/// Extraído dos handlers porque tem exatamente dois consumidores reais nesta
/// change (criação e atualização), que é a régua da convenção 2 — e porque
/// duplicar a ordem das operações nos dois seria justamente o jeito de as duas
/// divergirem depois.
/// </summary>
public sealed class KnowledgeContentProcessor(IServiceProvider serviceProvider)
{
    public const string SourceTypeErrorKey = "sourceType";
    public const string ContentErrorKey = "content";

    /// <summary>
    /// Ordem fixada em design.md (D5): **extrai primeiro, valida o teto
    /// depois**, sobre o texto já extraído. A extração encolhe o conteúdo (BOM,
    /// CRLF → LF); validar a entrada crua e expor o tamanho do texto extraído
    /// faria os dois números medirem strings diferentes, e um documento com
    /// muitas linhas poderia ser rejeitado a 1 MiB bruto enquanto o que seria
    /// persistido cabia.
    /// </summary>
    public KnowledgeContentResult Process(string sourceType, string rawContent)
    {
        var extractor = serviceProvider.GetKeyedService<IKnowledgeSourceExtractor>(sourceType);
        if (extractor is null)
        {
            return KnowledgeContentResult.Invalid(SourceTypeErrorKey,
                $"Tipo de origem '{sourceType}' não é suportado. Valores aceitos: {string.Join(", ", KnowledgeSourceTypes.All)}.");
        }

        var extraction = extractor.Extract(rawContent);
        if (!extraction.Succeeded)
        {
            return KnowledgeContentResult.Invalid(ContentErrorKey, extraction.FailureMessage!);
        }

        var extractedText = extraction.Text!;
        var byteCount = Encoding.UTF8.GetByteCount(extractedText);
        if (byteCount > KnowledgeDocumentLimits.MaxContentBytes)
        {
            return KnowledgeContentResult.Invalid(ContentErrorKey,
                $"O conteúdo do documento tem {byteCount} bytes e excede o limite de {KnowledgeDocumentLimits.MaxContentBytes} bytes.");
        }

        return KnowledgeContentResult.Success(extractedText);
    }
}

public sealed record KnowledgeContentResult(string? ExtractedText, Dictionary<string, string[]>? ValidationErrors)
{
    public bool Succeeded => ValidationErrors is null;

    public static KnowledgeContentResult Success(string extractedText) => new(extractedText, null);

    public static KnowledgeContentResult Invalid(string key, string message) =>
        new(null, new Dictionary<string, string[]> { [key] = [message] });
}
