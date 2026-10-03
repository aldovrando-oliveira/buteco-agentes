using Mediator;

namespace Buteco.Api.KnowledgeSync.Commands.DeleteSyncedDocument;

/// <summary>
/// Com a base sincronizada, a exclusão é idempotente e só tem um desfecho além do
/// sucesso: perder duas vezes seguidas para uma escrita concorrente do mesmo documento
/// (D10).
/// </summary>
public sealed record DeleteSyncedDocumentCommand(Guid KnowledgeBaseId, string ExternalRef, string Author)
    : ICommand<DeleteSyncedDocumentResult>;

public sealed record DeleteSyncedDocumentResult(SyncedKnowledgeBaseLookup Lookup, bool ConcurrentWriteConflict = false);
