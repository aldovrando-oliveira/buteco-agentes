namespace Buteco.Api.AgentKnowledgeBindings.Requests;

/// <summary>
/// <see cref="KnowledgeBaseIds"/> é nullable de propósito: campo ausente no
/// corpo precisa virar 400 explícito, não lista vazia silenciosa que removeria
/// todos os vínculos do agente (mesmo idioma de
/// <c>ReplaceAgentDelegationsRequest</c>).
/// </summary>
public sealed record ReplaceAgentKnowledgeBasesRequest(IReadOnlyList<Guid>? KnowledgeBaseIds);
