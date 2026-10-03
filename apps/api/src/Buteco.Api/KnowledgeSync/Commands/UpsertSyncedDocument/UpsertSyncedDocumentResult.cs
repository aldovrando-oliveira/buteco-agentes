using Buteco.Api.KnowledgeSync.Responses;

namespace Buteco.Api.KnowledgeSync.Commands.UpsertSyncedDocument;

public sealed record UpsertSyncedDocumentResult(
    SyncedKnowledgeBaseLookup Lookup,
    UpsertSyncedDocumentResponse? Response,
    Dictionary<string, string[]>? ValidationErrors,
    bool ConcurrentWriteConflict = false)
{
    /// <summary>
    /// Duas falhas de concorrência seguidas (D10): nada foi gravado, e a escrita pode
    /// ser repetida.
    /// </summary>
    public static UpsertSyncedDocumentResult ConcurrentWrite() =>
        new(SyncedKnowledgeBaseLookup.Synced, null, null, ConcurrentWriteConflict: true);

    public static UpsertSyncedDocumentResult NotSynced(SyncedKnowledgeBaseLookup lookup) => new(lookup, null, null);

    public static UpsertSyncedDocumentResult Invalid(Dictionary<string, string[]> errors) =>
        new(SyncedKnowledgeBaseLookup.Synced, null, errors);

    public static UpsertSyncedDocumentResult Success(Guid documentId, SyncedDocumentUpsertOutcome outcome) =>
        new(SyncedKnowledgeBaseLookup.Synced, new UpsertSyncedDocumentResponse(documentId, outcome), null);
}
