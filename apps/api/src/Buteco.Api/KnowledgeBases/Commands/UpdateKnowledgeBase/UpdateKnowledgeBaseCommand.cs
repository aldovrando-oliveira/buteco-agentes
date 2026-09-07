using Buteco.Api.KnowledgeBases.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeBases.Commands.UpdateKnowledgeBase;

public sealed record UpdateKnowledgeBaseCommand(Guid Id, string Name, string Description) : ICommand<KnowledgeBaseResponse?>;
