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
            return KnowledgeContentResult.Refused(new KnowledgeContentRefusal(
                SourceTypeErrorKey,
                KnowledgeContentRefusalCodes.UnsupportedSourceType,
                $"Tipo de origem '{sourceType}' não é suportado. Valores aceitos: {string.Join(", ", KnowledgeSourceTypes.All)}."));
        }

        var extraction = extractor.Extract(rawContent);
        if (!extraction.Succeeded)
        {
            return KnowledgeContentResult.Refused(
                new KnowledgeContentRefusal(ContentErrorKey, extraction.FailureCode!, extraction.FailureMessage!));
        }

        var extractedText = extraction.Text!;
        var byteCount = Encoding.UTF8.GetByteCount(extractedText);
        if (byteCount > KnowledgeDocumentLimits.MaxContentBytes)
        {
            return KnowledgeContentResult.Refused(new KnowledgeContentRefusal(
                ContentErrorKey,
                KnowledgeContentRefusalCodes.TooLarge,
                $"O conteúdo do documento tem {byteCount} bytes e excede o limite de {KnowledgeDocumentLimits.MaxContentBytes} bytes.",
                ContentBytes: byteCount,
                MaxContentBytes: KnowledgeDocumentLimits.MaxContentBytes));
        }

        return KnowledgeContentResult.Success(extractedText);
    }
}

public sealed record KnowledgeContentResult(string? ExtractedText, KnowledgeContentRefusal? Refusal)
{
    public bool Succeeded => Refusal is null;

    public static KnowledgeContentResult Success(string extractedText) => new(extractedText, null);

    public static KnowledgeContentResult Refused(KnowledgeContentRefusal refusal) => new(null, refusal);
}

/// <summary>
/// Uma recusa de conteúdo, tipada do extrator até o endpoint (design.md da change
/// codigo-recusa-conteudo-upsert, D5): a chave e a mensagem de <c>errors</c> que já
/// existiam, mais o código, que é o sinal. O tamanho e o teto só existem no
/// <c>too-large</c>, como números, para ninguém precisar extraí-los da frase.
/// </summary>
public sealed record KnowledgeContentRefusal(
    string ErrorKey,
    string Code,
    string Message,
    int? ContentBytes = null,
    int? MaxContentBytes = null);
