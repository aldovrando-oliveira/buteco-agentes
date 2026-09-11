using Buteco.Api.KnowledgeDocuments.Entities;

namespace Buteco.Api.KnowledgeDocuments.Responses;

/// <summary>
/// Documento completo: todos os campos da listagem **mais** o conteúdo. O
/// conteúdo é o que preenche a textarea da edição manual, e os campos da
/// listagem vêm junto para que a tela de edição não precise de uma segunda
/// requisição nem recalcule o tamanho por conta própria.
/// </summary>
public sealed record KnowledgeDocumentResponse(
    Guid Id,
    Guid KnowledgeBaseId,
    string Title,
    string SourceType,
    string ExtractedText,
    int ContentLengthBytes,
    KnowledgeIndexingStatus IndexingStatus,
    DateTimeOffset? IndexedAt,
    string? FailureReason,
    int ContentRevision,
    int FragmentCount,
    int IndexingAttempts,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static KnowledgeDocumentResponse FromEntity(KnowledgeDocument document) => new(
        document.Id,
        document.KnowledgeBaseId,
        document.Title,
        document.SourceType,
        document.ExtractedText,
        document.ContentLengthBytes,
        document.IndexingStatus,
        document.IndexedAt,
        document.FailureReason,
        document.ContentRevision,
        document.FragmentCount,
        document.IndexingAttempts,
        document.LastAttemptAt,
        document.CreatedAt,
        document.UpdatedAt);
}
