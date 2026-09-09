using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.AgentKnowledgeBindings;

/// <summary>
/// Consulta compartilhada das bases de conhecimento vinculadas a um agente,
/// usada por todos os handlers de <c>Agents</c> que retornam
/// <c>AgentResponse</c> (design.md, D7 — id + name, não o registro completo).
/// </summary>
public static class AgentKnowledgeBaseLookup
{
    public static async Task<IReadOnlyList<KnowledgeBaseSummaryResponse>> GetLinkedKnowledgeBasesAsync(
        AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken)
    {
        return await dbContext.AgentKnowledgeBases
            .AsNoTracking()
            .Where(binding => binding.AgentId == agentId)
            .Join(dbContext.KnowledgeBases, binding => binding.KnowledgeBaseId, knowledgeBase => knowledgeBase.Id, (binding, knowledgeBase) => knowledgeBase)
            // O ThenBy NÃO é redundante (design.md, D13). Nome de base de
            // conhecimento não é único *por requisito*
            // (knowledge-base-catalog, cenário "Nome duplicado é permitido"),
            // então OrderBy(Name) sozinho deixa a ordem entre homônimas a
            // cargo do plano do Postgres: a mesma requisição pode responder em
            // ordens diferentes sem nada ter mudado no cadastro. É a mesma
            // classe de defeito que dedupe-global-nome-de-tool corrigiu em
            // McpToolSetResolver — lá por Id em vez de Name, porque nada é
            // exibido; aqui Name é o critério que serve ao operador e o Id é o
            // desempate.
            .OrderBy(knowledgeBase => knowledgeBase.Name)
            .ThenBy(knowledgeBase => knowledgeBase.Id)
            .Select(knowledgeBase => KnowledgeBaseSummaryResponse.FromEntity(knowledgeBase))
            .ToListAsync(cancellationToken);
    }
}
