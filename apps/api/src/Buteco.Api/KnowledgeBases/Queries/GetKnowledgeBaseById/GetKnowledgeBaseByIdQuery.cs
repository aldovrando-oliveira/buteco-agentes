using Buteco.Api.KnowledgeBases.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeBases.Queries.GetKnowledgeBaseById;

public sealed record GetKnowledgeBaseByIdQuery(Guid Id) : IQuery<KnowledgeBaseResponse?>;
