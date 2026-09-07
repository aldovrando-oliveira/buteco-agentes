using Buteco.Api.KnowledgeBases.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeBases.Commands.CreateKnowledgeBase;

public sealed record CreateKnowledgeBaseCommand(string Name, string Description) : ICommand<KnowledgeBaseResponse>;
