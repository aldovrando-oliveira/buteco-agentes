using Buteco.Api.KnowledgeDocuments.Entities;

namespace Buteco.Api.KnowledgeDocuments.Responses;

/// <summary>
/// Um evento do histórico, no fio em camelCase. <c>type</c> sai como string
/// (<see cref="KnowledgeDocumentEventType"/> tem <c>JsonStringEnumConverter</c>).
/// <c>contentChanged</c> e <c>titleChanged</c> são nulos fora de
/// <c>Updated</c> (design.md da change historico-documentos-base, D5).
/// </summary>
public sealed record KnowledgeDocumentEventResponse(
    Guid Id,
    Guid DocumentId,
    string DocumentTitle,
    KnowledgeDocumentEventType Type,
    bool? ContentChanged,
    bool? TitleChanged,
    string Author,
    DateTimeOffset OccurredAt);
