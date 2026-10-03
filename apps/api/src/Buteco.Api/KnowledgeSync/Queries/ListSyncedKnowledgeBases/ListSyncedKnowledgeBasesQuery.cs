using Buteco.Api.KnowledgeSync.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeSync.Queries.ListSyncedKnowledgeBases;

public sealed record ListSyncedKnowledgeBasesQuery : IQuery<IReadOnlyList<SyncedKnowledgeBaseResponse>>;
