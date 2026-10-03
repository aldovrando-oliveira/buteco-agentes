using Buteco.Api.KnowledgeBases.Responses;

namespace Buteco.Api.KnowledgeSync.Commands.RecordSyncResult;

public sealed record RecordSyncResultResult(SyncedKnowledgeBaseLookup Lookup, KnowledgeBaseResponse? KnowledgeBase);
