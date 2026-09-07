using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeDocuments.Queries.GetKnowledgeDocumentById;

public sealed record GetKnowledgeDocumentByIdQuery(Guid KnowledgeBaseId, Guid Id)
    : IQuery<KnowledgeDocumentResponse?>;
