using Mediator;

namespace Buteco.Api.KnowledgeDocuments.Commands.DeleteKnowledgeDocument;

public sealed record DeleteKnowledgeDocumentCommand(Guid KnowledgeBaseId, Guid Id) : ICommand<bool>;
