using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeDocuments.Queries.ListKnowledgeDocuments;

public sealed record ListKnowledgeDocumentsQuery(Guid KnowledgeBaseId)
    : IQuery<IReadOnlyList<KnowledgeDocumentSummaryResponse>?>;
