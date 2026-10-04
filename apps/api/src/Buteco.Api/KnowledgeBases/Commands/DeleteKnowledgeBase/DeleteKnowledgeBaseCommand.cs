using Mediator;

namespace Buteco.Api.KnowledgeBases.Commands.DeleteKnowledgeBase;

public sealed record DeleteKnowledgeBaseCommand(Guid Id) : ICommand<DeleteKnowledgeBaseResult>;
