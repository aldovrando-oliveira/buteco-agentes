namespace Buteco.Api.KnowledgeBases.Requests;

/// <summary>
/// <c>ContentMode</c> é string, e não o enum, para que valor desconhecido vire
/// <c>ValidationProblem</c> em <c>contentMode</c>, no mesmo formato das outras
/// recusas, em vez do 400 genérico de desserialização (design.md da change
/// catalogo-base-sincronizada, D11).
/// </summary>
public record CreateKnowledgeBaseRequest(string? Name, string? Description, string? ContentMode = null);
