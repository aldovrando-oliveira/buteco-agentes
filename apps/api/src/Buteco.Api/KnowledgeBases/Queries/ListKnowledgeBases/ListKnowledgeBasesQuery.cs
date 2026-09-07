using Buteco.Api.KnowledgeBases.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeBases.Queries.ListKnowledgeBases;

public sealed record ListKnowledgeBasesQuery : IQuery<IReadOnlyList<KnowledgeBaseResponse>>;
