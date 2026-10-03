using Buteco.Api.KnowledgeSync.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeSync.Queries.ListSyncedDocumentRefs;

public sealed record ListSyncedDocumentRefsQuery(Guid KnowledgeBaseId) : IQuery<ListSyncedDocumentRefsResult>;

public sealed record ListSyncedDocumentRefsResult(
    SyncedKnowledgeBaseLookup Lookup,
    IReadOnlyList<SyncedDocumentRefResponse>? Documents);
