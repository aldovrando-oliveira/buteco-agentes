using Buteco.Api.KnowledgeBases.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeBases.Commands.DeactivateKnowledgeBase;

public sealed record DeactivateKnowledgeBaseCommand(Guid Id) : ICommand<KnowledgeBaseResponse?>;
