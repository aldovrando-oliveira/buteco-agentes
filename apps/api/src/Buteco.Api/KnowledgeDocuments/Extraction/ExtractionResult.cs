namespace Buteco.Api.KnowledgeDocuments.Extraction;

/// <summary>
/// Resultado da extração de um conteúdo cru. Rejeição é caminho normal, não
/// exceção: vira HTTP 400 com <c>ValidationProblemDetails</c> no endpoint.
/// </summary>
public sealed record ExtractionResult(bool Succeeded, string? Text, string? FailureMessage)
{
    public static ExtractionResult Success(string text) => new(true, text, null);

    public static ExtractionResult Failure(string failureMessage) => new(false, null, failureMessage);
}
