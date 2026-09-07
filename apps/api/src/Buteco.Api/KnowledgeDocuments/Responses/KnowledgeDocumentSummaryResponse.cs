using Buteco.Api.KnowledgeDocuments.Entities;

namespace Buteco.Api.KnowledgeDocuments.Responses;

/// <summary>
/// Item da listagem. **Não inclui <c>ExtractedText</c>** (design.md, D14): uma
/// base com 50 documentos de 80 KB devolveria 4 MB de texto que nenhuma tela
/// usa.
///
/// <c>ContentLengthBytes</c> vem da coluna gerada pelo Postgres, na mesma
/// unidade do teto validado no cadastro — nunca <c>ExtractedText.Length</c>,
/// que traduziria para <c>length()</c> e contaria caracteres.
/// </summary>
public sealed record KnowledgeDocumentSummaryResponse(
    Guid Id,
    Guid KnowledgeBaseId,
    string Title,
    string SourceType,
    int ContentLengthBytes,
    KnowledgeIndexingStatus IndexingStatus,
    DateTimeOffset? IndexedAt,
    string? FailureReason,
    int ContentRevision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
