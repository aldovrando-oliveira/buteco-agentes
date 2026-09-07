using Mediator;

namespace Buteco.Api.KnowledgeDocuments.Commands.UpdateKnowledgeDocument;

public sealed record UpdateKnowledgeDocumentCommand(
    Guid KnowledgeBaseId,
    Guid Id,
    string Title,
    string SourceType,
    string Content) : ICommand<UpdateKnowledgeDocumentResult>;
