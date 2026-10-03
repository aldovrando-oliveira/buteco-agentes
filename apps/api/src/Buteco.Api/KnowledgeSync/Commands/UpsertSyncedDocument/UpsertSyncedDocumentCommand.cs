using Mediator;

namespace Buteco.Api.KnowledgeSync.Commands.UpsertSyncedDocument;

public sealed record UpsertSyncedDocumentCommand(
    Guid KnowledgeBaseId,
    string ExternalRef,
    string ExternalVersion,
    string Title,
    string SourceType,
    string Content,
    string Author) : ICommand<UpsertSyncedDocumentResult>;
