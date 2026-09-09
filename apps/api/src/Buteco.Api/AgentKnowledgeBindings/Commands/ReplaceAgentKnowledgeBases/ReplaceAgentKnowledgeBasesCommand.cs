using Mediator;

namespace Buteco.Api.AgentKnowledgeBindings.Commands.ReplaceAgentKnowledgeBases;

public sealed record ReplaceAgentKnowledgeBasesCommand(Guid AgentId, IReadOnlyList<Guid> KnowledgeBaseIds) : ICommand<ReplaceAgentKnowledgeBasesResult>;
