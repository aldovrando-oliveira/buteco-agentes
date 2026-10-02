using Mediator;

namespace Buteco.Api.KnowledgeDocuments.Commands.CreateKnowledgeDocument;

public sealed record CreateKnowledgeDocumentCommand(
    Guid KnowledgeBaseId,
    string Title,
    string SourceType,
    string Content,
    string Author) : ICommand<CreateKnowledgeDocumentResult>;
