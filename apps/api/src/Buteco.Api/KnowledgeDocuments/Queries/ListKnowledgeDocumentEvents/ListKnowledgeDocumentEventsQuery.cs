using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeDocuments.Queries.ListKnowledgeDocumentEvents;

/// <summary>
/// <paramref name="After"/> nulo pede a primeira página. O cursor já chega
/// decodificado: a validação do formato é do endpoint, que responde <c>400</c>.
/// </summary>
public sealed record ListKnowledgeDocumentEventsQuery(Guid KnowledgeBaseId, KnowledgeDocumentEventCursor? After)
    : IQuery<KnowledgeDocumentEventPageResponse?>;
