import type { AgentDelegationInsights } from '../types/agentInsights';

// O CRUZAMENTO DOS DOIS LADOS DA DELEGAÇÃO — AGREGADO × CATÁLOGO.
//
// ===========================================================================
// OS DOIS LADOS NÃO SÃO ESPELHO, E ESTE MÓDULO NÃO OS RECONCILIA.
// ===========================================================================
//
// `delegatesTo` é o que o agente TENTOU (`delegation_outcomes` pelo agente de
// ORIGEM); `triggeredBy` é o que de fato RODOU nele (`task_executions` pelo
// agente de DESTINO). São perguntas diferentes sobre a mesma relação, e os dois
// lados PODEM mostrar números diferentes para os mesmos dois agentes.
//
// Duas causas, independentes, e nenhuma delas é ruído de borda:
//   1. resultado que não produz execução — `NotStarted` nunca cria task no
//      destino, e `Expired` pode ter criado uma que nunca rodou;
//   2. DOIS RELÓGIOS — um lado é situado pela execução de origem, o outro pela
//      de destino. Uma delegação às 23:58 cujo destino roda às 00:03 cai em
//      dias diferentes do balde. **Esta não some com período maior.**
//
// Por isso este módulo devolve DOIS lados independentes e NÃO expõe nenhum
// total agregado dos dois: um campo assim seria a soma que a tela não pode
// fazer, e ele apareceria na tela no dia em que alguém achasse que "fica mais
// completo".
//
// ===========================================================================
// CADASTRO E USO SÃO FATOS DIFERENTES, E É AQUI QUE A DISTINÇÃO VIVE.
// ===========================================================================
//
// O `Agente-Delegado.dc.html` escreve a regra: *"'sem delegação cadastrada' não
// é o mesmo que um vínculo que existe e não foi usado no período, que apareceria
// como linha com contagem zero."* É a gramática dos quatro estados aplicada a
// LINHAS em vez de células:
//
//   | situação                          | o que sai daqui                     |
//   |-----------------------------------|-------------------------------------|
//   | cadastrado E com ocorrência       | linha com a contagem medida         |
//   | cadastrado E sem ocorrência       | linha com `0` — contagem FEITA      |
//   | sem cadastro nenhum               | NENHUMA linha → a tela desenha o    |
//   |                                   | tracejado, sem número               |
//   | ocorrência medida, cadastro fora  | linha com a contagem, `registered`  |
//   |                                   | falso — a medição aconteceu         |
//
// "Sem cadastro nenhum" é `rows.length === 0`, e não precisa de bandeira: se há
// cadastro, há linha; se há medição, há linha. A lista vazia só acontece quando
// não há nem um nem outro, e é exatamente o estado que o tracejado nomeia.
//
// ===========================================================================
// O CATÁLOGO ENTRA POR FORMA ESTRUTURAL, NÃO POR IMPORT DA OUTRA FEATURE.
// ===========================================================================
//
// `DelegationCatalogAgent` declara o que ESTA função precisa, e `Agent[]` de
// `features/agents` o satisfaz estruturalmente. A seta entre as features
// continua de mão única — `agents` importa `insights`, nunca o contrário —, e o
// módulo fica testável sem montar um agente inteiro.
//
// O cadastro de ENTRADA é derivado varrendo o `delegatesTo` do catálogo.
// Conferido em `ListAgentsQueryHandler` (`apps/api`): `GET /agents` devolve o
// `AgentResponse` completo, com `DelegatesTo` por agente — não há rota nova a
// pedir, e nenhuma requisição além das que a página de detalhe já faz.

/** O mínimo que o cruzamento precisa de um agente do catálogo. */
export interface DelegationCatalogAgent {
  id: string;
  name: string;
  delegatesTo: { id: string; name: string }[];
}

export interface DelegationOutcomeCount {
  outcome: string;
  count: number;
}

export interface DelegatesToRowView {
  agentId: string;
  /** `null` quando o catálogo não respondeu ou não conhece o agente. */
  name: string | null;
  /** A soma das contagens dos resultados. `0` é contagem FEITA. */
  total: number;
  /** Discriminado por resultado. Vazio no vínculo cadastrado e ocioso. */
  outcomes: DelegationOutcomeCount[];
  /** `false` quando houve medição e o vínculo não está mais no cadastro. */
  registered: boolean;
}

export interface TriggeredByRowView {
  agentId: string;
  name: string | null;
  executedCount: number;
  registered: boolean;
}

export interface DelegationSides {
  delegatesTo: DelegatesToRowView[];
  triggeredBy: TriggeredByRowView[];
  /** O maior total do lado de saída, para a barra. `0` quando não há nenhum. */
  delegatesToMax: number;
  triggeredByMax: number;
}

/**
 * Ordem: contagem maior primeiro, depois nome, depois identificador.
 *
 * O desempate por nome e por id é o que torna a ordem ESTÁVEL entre renders —
 * sem ele, dois destinos com a mesma contagem trocariam de lugar a cada
 * resposta, e o operador leria movimento onde não houve. Mesmo espírito do
 * desempate que `api-response-ordering` fixa no servidor, aqui no cliente
 * porque a ordenação é do cliente.
 *
 * A comparação de nome é `localeCompare` em `pt-BR`: esta ordenação nunca é
 * comparada com uma do Postgres, então a discordância de collation registrada
 * em `ordenacao-desempate-listas-vinculo` não alcança este ponto.
 */
function compareRows(
  a: { total: number; name: string | null; agentId: string },
  b: { total: number; name: string | null; agentId: string },
): number {
  if (a.total !== b.total) {
    return b.total - a.total;
  }
  const nomeA = a.name ?? a.agentId;
  const nomeB = b.name ?? b.agentId;
  const porNome = nomeA.localeCompare(nomeB, 'pt-BR');
  return porNome !== 0 ? porNome : a.agentId.localeCompare(b.agentId);
}

/**
 * Cruza o agregado com o catálogo e devolve os dois lados prontos para
 * apresentação.
 *
 * @param delegation  o bloco `delegation` da resposta
 * @param agentId     o agente consultado
 * @param registeredTargets  o `delegatesTo` do próprio agente, como o detalhe
 *                    já o traz. É o cadastro de SAÍDA.
 * @param catalog     o catálogo inteiro, ou `undefined` enquanto ele não
 *                    respondeu. Sem ele, as linhas continuam todas lá — só o
 *                    nome fica nulo, e o cadastro de ENTRADA não é derivável.
 */
export function delegationSides(
  delegation: AgentDelegationInsights,
  agentId: string,
  registeredTargets: { id: string; name: string }[],
  catalog: DelegationCatalogAgent[] | undefined,
): DelegationSides {
  const nomes = new Map<string, string>();
  for (const agente of catalog ?? []) {
    nomes.set(agente.id, agente.name);
  }
  // O cadastro de saída também carrega nome, e ele vale mesmo sem catálogo.
  for (const alvo of registeredTargets) {
    nomes.set(alvo.id, alvo.name);
  }
  const nomeDe = (id: string): string | null => nomes.get(id) ?? null;

  // ------------------------------------------------------------ delega para
  const porDestino = new Map<string, DelegationOutcomeCount[]>();
  for (const linha of delegation.delegatesTo) {
    const atual = porDestino.get(linha.targetAgentId);
    if (atual === undefined) {
      porDestino.set(linha.targetAgentId, [{ outcome: linha.outcome, count: linha.count }]);
    } else {
      atual.push({ outcome: linha.outcome, count: linha.count });
    }
  }

  const alvosCadastrados = new Set(registeredTargets.map((a) => a.id));
  // A união é o ponto: o cadastrado sem medição entra com `0`, e o medido sem
  // cadastro entra com a contagem. Nenhum dos dois é descartado.
  const idsDeSaida = new Set<string>([...alvosCadastrados, ...porDestino.keys()]);

  const saida: DelegatesToRowView[] = [...idsDeSaida].map((id) => {
    const outcomes = porDestino.get(id) ?? [];
    return {
      agentId: id,
      name: nomeDe(id),
      total: outcomes.reduce((soma, o) => soma + o.count, 0),
      // Ordem do detalhe: maior primeiro, depois o nome do resultado, para que
      // a linha não dance entre renders.
      outcomes: [...outcomes].sort(
        (a, b) => b.count - a.count || a.outcome.localeCompare(b.outcome),
      ),
      registered: alvosCadastrados.has(id),
    };
  });
  saida.sort(compareRows);

  // ----------------------------------------------------------- acionado por
  const medidoPorOrigem = new Map<string, number>();
  for (const linha of delegation.triggeredBy) {
    medidoPorOrigem.set(
      linha.sourceAgentId,
      (medidoPorOrigem.get(linha.sourceAgentId) ?? 0) + linha.executedCount,
    );
  }

  // O cadastro de ENTRADA não existe como campo: é derivado de quem, no
  // catálogo, declara este agente como destino. Sem catálogo não há como
  // derivá-lo, e aí só aparecem as origens MEDIDAS — que é honesto: a tela não
  // sabe o cadastro, e não inventa "sem vínculo".
  const origensCadastradas = new Set(
    (catalog ?? [])
      .filter((a) => a.id !== agentId && a.delegatesTo.some((d) => d.id === agentId))
      .map((a) => a.id),
  );
  const idsDeEntrada = new Set<string>([...origensCadastradas, ...medidoPorOrigem.keys()]);

  const entrada: TriggeredByRowView[] = [...idsDeEntrada].map((id) => ({
    agentId: id,
    name: nomeDe(id),
    executedCount: medidoPorOrigem.get(id) ?? 0,
    registered: origensCadastradas.has(id),
  }));
  entrada.sort((a, b) =>
    compareRows(
      { total: a.executedCount, name: a.name, agentId: a.agentId },
      { total: b.executedCount, name: b.name, agentId: b.agentId },
    ),
  );

  return {
    delegatesTo: saida,
    triggeredBy: entrada,
    delegatesToMax: Math.max(0, ...saida.map((r) => r.total)),
    triggeredByMax: Math.max(0, ...entrada.map((r) => r.executedCount)),
  };
}
