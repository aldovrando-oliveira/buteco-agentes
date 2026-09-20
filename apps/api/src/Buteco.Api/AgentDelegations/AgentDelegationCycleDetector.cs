namespace Buteco.Api.AgentDelegations;

/// <summary>
/// Acha um ciclo de delegação que volte a um agente de origem, no grafo de
/// <c>agent_delegations</c>.
///
/// <para>
/// <b>Por que isto existe.</b> Um ciclo <c>A→B→…→A</c> autotrava no advisory
/// lock de contexto de <c>apps/workers</c>: a task de A em profundidade 0 segura
/// <c>pg_advisory_lock(hashtext(A), hashtext(contexto))</c> enquanto espera B, e
/// a task de A em profundidade 2 bloqueia no mesmo lock. Medido na exploração
/// <c>replicas-de-worker</c> com <b>quatro</b> instâncias de worker: réplica
/// nenhuma resolve, porque o recurso disputado é o lock e não o consumidor.
/// </para>
///
/// <para>
/// <b>Por que a travessia é completa, e não de N saltos fixo.</b> O critério da
/// decisão foi a clareza da regra, não o custo (design.md, D1). Um ciclo de três
/// saltos autotrava exatamente igual ao de dois; uma regra que bloqueia alguns
/// travamentos e libera outros é pior que nenhuma, porque quem cadastrou confia
/// nela. O <c>DelegationDepthLimit</c> (5, em
/// <c>apps/workers/…/AgentExecutionService.cs</c>) <b>não</b> é rede de
/// segurança: a checagem de profundidade roda ANTES da aquisição do lock, então
/// o ciclo curto passa por ela e trava mesmo assim.
/// </para>
///
/// <para>
/// <b>Função pura, sem acesso a banco.</b> Quem chama carrega as arestas numa
/// consulta só e monta aqui o grafo <b>pós-replace</b> — é isso que permite
/// testar a travessia sem infraestrutura, e é isso que faz o handler custar uma
/// consulta em vez de uma por salto.
/// </para>
///
/// <para>
/// <b>Custo, com o escopo colado ao número</b> (convenção 22): a travessia é
/// linear no número de arestas de <c>agent_delegations</c>, lidas inteiras numa
/// consulta de duas colunas <c>uuid</c>. O único número de referência disponível
/// é <b>zero linhas em <c>agent_delegations</c>, medido no banco de
/// desenvolvimento em 20/09/2026</b> — e ele é sobre <b>volume de dado</b>, não
/// sobre latência de busca. Citá-lo como referência de tempo de resposta seria a
/// ocorrência 3 da convenção 22, em que o sistema não muda e muda a pergunta
/// feita ao número. <b>Gatilho de recalibração:</b> a primeira medição que
/// encontre <c>agent_delegations</c> na casa das dezenas de milhares de linhas —
/// aí a travessia migra para CTE recursiva, e quem fizer a medição recalibra.
/// </para>
/// </summary>
public static class AgentDelegationCycleDetector
{
    /// <summary>
    /// Devolve o caminho de delegações que fecha um ciclo de volta a
    /// <paramref name="originAgentId"/>, começando e terminando nele
    /// (<c>[A, B, C, A]</c>), ou <c>null</c> quando não houver ciclo.
    /// </summary>
    /// <param name="edges">
    /// O grafo <b>já resolvido para depois do replace</b>: as arestas de saída
    /// do agente de origem são as do payload, não as que estão no banco. Ver
    /// <see cref="BuildGraph"/>.
    /// </param>
    public static IReadOnlyList<Guid>? FindCycleFrom(
        Guid originAgentId,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> edges)
    {
        // Busca em profundidade com o caminho na pilha, para poder devolver o
        // caminho e não só o fato. `visited` é global à travessia: um agente já
        // percorrido sem achar a origem não volta a ser percorrido, o que mantém
        // a travessia linear mesmo num grafo com muitos caminhos convergentes.
        var visited = new HashSet<Guid>();
        var path = new List<Guid> { originAgentId };

        return Walk(originAgentId) ? path : null;

        bool Walk(Guid current)
        {
            if (!edges.TryGetValue(current, out var targets))
            {
                return false;
            }

            foreach (var target in targets)
            {
                path.Add(target);

                // Voltar à origem é o ciclo que interessa. Um ciclo entre
                // terceiros que não passe pela origem não é problema DESTE
                // salvamento: ele só pode existir se já estava no banco, e quem
                // o desfaz é o agente dele — recusar aqui seria prender o
                // operador por um ciclo que ele não está criando.
                if (target == originAgentId)
                {
                    return true;
                }

                if (visited.Add(target) && Walk(target))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
            }

            return false;
        }
    }

    /// <summary>
    /// Monta a lista de adjacência <b>como ela ficaria depois do replace</b>:
    /// todas as arestas de saída de <paramref name="originAgentId"/> que estão no
    /// banco são descartadas e as de <paramref name="replacementTargets"/> entram
    /// no lugar delas.
    ///
    /// <para>
    /// Não é detalhe: a operação é <c>replace</c>, e percorrer o grafo ATUAL
    /// recusaria a edição que <b>desfaz</b> um ciclo — por causa do ciclo que ela
    /// está desfazendo. O operador que herdasse um ciclo ficaria preso nele
    /// (design.md, D7). <c>ReplaceAgentDelegations_UndoingInheritedCycle_IsPermitted</c>
    /// é o guarda que prende isto, e ele <b>passa em <c>HEAD</c></b> de
    /// propósito: é de regressão contra a correção errada, não contra o defeito.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> BuildGraph(
        IEnumerable<(Guid SourceAgentId, Guid TargetAgentId)> persistedEdges,
        Guid originAgentId,
        IReadOnlyList<Guid> replacementTargets)
    {
        var graph = new Dictionary<Guid, List<Guid>>();

        foreach (var (source, target) in persistedEdges)
        {
            if (source == originAgentId)
            {
                continue;
            }

            if (!graph.TryGetValue(source, out var targets))
            {
                targets = [];
                graph[source] = targets;
            }

            targets.Add(target);
        }

        if (replacementTargets.Count > 0)
        {
            graph[originAgentId] = [.. replacementTargets];
        }

        return graph.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<Guid>)entry.Value);
    }
}
