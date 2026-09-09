using Buteco.Api.KnowledgeBases.Entities;

namespace Buteco.Api.KnowledgeBases.Responses;

/// <summary>
/// Nível de detalhe usado em <c>AgentResponse.KnowledgeBases</c> (design.md,
/// D7) — id + name, não o registro completo da <c>KnowledgeBase</c>, mesmo
/// nível de <c>McpServerSummaryResponse</c> e <c>AgentSummaryResponse</c>.
/// </summary>
/// <remarks>
/// <b>Sem <c>isActive</c>, e isso é decisão</b>: a tela de vínculo tira o
/// estado do <i>catálogo</i> (<c>GET /knowledge-bases</c>, que ela já busca
/// para montar a lista de seleção), não do vínculo — é o que a aba de
/// Ferramentas já faz com <c>GET /mcp-servers</c>
/// (<c>agent-mcp-binding-ui</c>). Sem contagem de documentos pelo mesmo motivo
/// que a etapa 1 registrou: exigiria segunda consulta agregada por base.
/// </remarks>
public sealed record KnowledgeBaseSummaryResponse(Guid Id, string Name)
{
    public static KnowledgeBaseSummaryResponse FromEntity(KnowledgeBase knowledgeBase) => new(knowledgeBase.Id, knowledgeBase.Name);
}
