namespace Buteco.Workers.Knowledge.Entities;

/// <summary>
/// Espelho da entidade de vínculo de <c>apps/api</c>
/// (<c>Buteco.Api.AgentKnowledgeBindings.Entities.AgentKnowledgeBase</c>) —
/// sem <c>ProjectReference</c> cruzado e sem lib compartilhada, mesmo padrão já
/// usado para <c>McpServer</c>, <c>AgentDelegation</c> e as duas entidades de
/// conhecimento.
/// </summary>
/// <remarks>
/// Nesta etapa <b>nenhum</b> código de <c>apps/workers</c> lê esta tabela: o
/// resolvedor de conhecimento é a etapa 4. A entidade existe aqui para que os
/// dois <c>AppDbContext</c> não divirjam no intervalo — divergência que só
/// apareceria dentro de outra change (design.md, D11).
/// </remarks>
public class AgentKnowledgeBase
{
    public Guid AgentId { get; private set; }

    public Guid KnowledgeBaseId { get; private set; }

    private AgentKnowledgeBase()
    {
    }

    public AgentKnowledgeBase(Guid agentId, Guid knowledgeBaseId)
    {
        AgentId = agentId;
        KnowledgeBaseId = knowledgeBaseId;
    }
}
