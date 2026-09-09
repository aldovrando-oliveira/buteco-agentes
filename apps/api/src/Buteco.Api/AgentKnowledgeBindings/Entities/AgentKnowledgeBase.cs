namespace Buteco.Api.AgentKnowledgeBindings.Entities;

/// <summary>
/// Vínculo N:N entre <see cref="Buteco.Api.Agents.Entities.Agent"/> e
/// <see cref="Buteco.Api.KnowledgeBases.Entities.KnowledgeBase"/>. Tabela
/// relacional própria, não jsonb, porque os dois lados são entidades
/// catalogadas com identidade própria (design.md, D1 — mesmo idioma de
/// <c>AgentDelegation</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A ausência de coluna extra é decisão, não esquecimento</b> (design.md,
/// D2). Não há análogo de <c>AgentMcpServer.AllowedTools</c> — aquela coluna
/// existe porque as tools de um servidor MCP <i>não são catálogo persistido em
/// lugar nenhum</i> (são descobertas ao vivo via <c>tools/list</c>), então a
/// seleção precisa morar no vínculo. Bases de conhecimento têm id e são
/// catálogo persistido: a seleção é o próprio conjunto de ids, e não sobra
/// nada para uma coluna guardar.
/// </para>
/// <para>
/// <c>TopK</c> e limiar de similaridade ficam como constante em
/// <c>apps/workers</c> até haver dois consumidores reais querendo valores
/// diferentes (convenção 2). Não há flag "injetar sempre": o acesso é sob
/// demanda, decidido pelo modelo a partir de
/// <c>KnowledgeBase.Description</c>.
/// </para>
/// <para>
/// Não existe auto-vínculo a rejeitar, ao contrário de <c>AgentDelegation</c>:
/// lá os dois lados são <c>Agent</c>, aqui são entidades diferentes.
/// </para>
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
