namespace Buteco.Api.KnowledgeDocuments.Extraction;

/// <summary>
/// Resultado da extração de um conteúdo cru. Rejeição é caminho normal, não
/// exceção: vira HTTP 400 com <c>ValidationProblemDetails</c> no endpoint.
/// </summary>
/// <remarks>
/// A falha exige o código junto com a mensagem (design.md da change
/// codigo-recusa-conteudo-upsert, D5): um extrator não consegue declarar recusa sem
/// código, e a #105 nunca precisa ler a frase.
/// </remarks>
public sealed record ExtractionResult(bool Succeeded, string? Text, string? FailureCode, string? FailureMessage)
{
    public static ExtractionResult Success(string text) => new(true, text, null, null);

    public static ExtractionResult Failure(string failureCode, string failureMessage) => new(false, null, failureCode, failureMessage);
}
