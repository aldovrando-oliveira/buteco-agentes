using Buteco.Api.KnowledgeBases.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeBases.Commands.ActivateKnowledgeBase;

public sealed record ActivateKnowledgeBaseCommand(Guid Id) : ICommand<KnowledgeBaseResponse?>;
