namespace Buteco.Api.KnowledgeDocuments.Requests;

/// <summary>
/// Mesmo shape para os dois caminhos do modal ("Subir arquivos" e "Escrever
/// manualmente"): o cliente lê o arquivo com <c>FileReader</c> e manda o texto
/// (design.md, D2/D3). Zero multipart nesta etapa.
/// </summary>
public record CreateKnowledgeDocumentRequest(string? Title, string? SourceType, string? Content);
