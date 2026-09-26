// O CONTRATO DE `GET /insights/agents/{id}`, TRANSCRITO DE
// `apps/api/src/Buteco.Api/Insights/Responses/AgentInsightsResponse.cs` E
// CONFERIDO CONTRA O CORPO REAL (tarefa 0.7 do `tasks.md`).
//
// ---------------------------------------------------------------------------
// ESTE NÃO É `SystemInsights` COM UM FILTRO, E O TIPO É A PRIMEIRA BARREIRA.
// ---------------------------------------------------------------------------
//
// Nove das 27 métricas do catálogo **não transferem por analogia** com o escopo
// do sistema: duas não existem, quatro são outra consulta, e quatro mudam de
// significado. O `.cs` tomou a mesma decisão e escreveu a razão: reaproveitar o
// tipo do sistema obrigaria a carregar `ByAgent` e `IndexingFailures` servidos
// como listas vazias — e **lista vazia é o texto de "medi e não achei nada"**,
// que afirmaria medição onde não há fonte. A convenção 13 seria furada pela
// FORMA do tipo, antes de qualquer consulta rodar.
//
// Por isso, as três ausências deste arquivo são contrato, não esquecimento:
//
//   1. NÃO HÁ `byAgent`. O recorte já é o agente; um agrupamento de um elemento
//      só com nome de comparação é a métrica do sistema ressuscitada por
//      descuido.
//   2. NÃO HÁ `indexingFailures`. `knowledge_indexing_attempts` tem documento e
//      base e **nenhuma coluna de agente** — indexação é trabalho da base.
//   3. `searchEmbeddingInputTokens` NÃO É `embeddingInputTokens`. Neste escopo o
//      número **não inclui a indexação**, e o nome do campo é o que impede o
//      rótulo do todo de cair sobre metade do dado. O caminho por
//      `AgentKnowledgeBases` foi RECUSADO na rota: o vínculo é de muitos para
//      muitos, e uma indexação que aconteceu UMA vez seria contada em cada
//      agente vinculado.
//
// Quem for acrescentar campo aqui confere antes contra o `.cs`: um campo que o
// tipo do sistema tem e este não tem é, quase sempre, uma das três acima.
//
// ---------------------------------------------------------------------------
// O QUE É IMPORTADO DO GÊMEO, DE PROPÓSITO
// ---------------------------------------------------------------------------
//
// As formas abaixo são **neutras de escopo** — significam a mesma coisa nas duas
// rotas, e o `.cs` as reusa pelo mesmo motivo. Duplicá-las criaria dois lugares
// onde o mesmo conceito pode divergir (convenção 2). É também o que permite
// `measuredDays` e `WeekdayActivityCard` serem reusados sem adaptador.
//
// ---------------------------------------------------------------------------
// TODO CAMPO ANULÁVEL ENTRA COMO `| null`, E NENHUM RECEBE DEFAULT.
// ---------------------------------------------------------------------------
//
// Primeira das três linhas de defesa da gramática dos quatro estados. Um
// `number` com `?? 0` aqui apagaria a distinção entre "não reportado" e
// "reportou zero" antes de qualquer componente ver o dado, e nenhum teste de
// apresentação conseguiria pegá-la depois. As outras duas são `metricState.ts`
// e as asserções negativas dos componentes.
//
// O que é anulável e o que não é vem do C#, não de palpite:
//   - `long?` / `double?` / `int?` → `number | null`
//   - `int` (contagem medida)      → `number`, e o zero dele é VERDADE
//   - `Guid`                       → `string`
//   - `string?`                    → `string | null`

import type {
  DurationStats,
  InsightsWindow,
  ModelTokens,
  NonTerminalTasks,
  TemporalInsights,
  TokenTotals,
  TokensPerTask,
  FailurePhaseCount,
} from './systemInsights';

export type {
  DurationStats,
  InsightsWindow,
  ModelTokens,
  NonTerminalTasks,
  TemporalInsights,
  TokenTotals,
  TokensPerTask,
  FailurePhaseCount,
};

/**
 * M1 e M2 recortadas pelo agente — e `delegationOriginTaskCount` é campo que o
 * escopo do sistema **não** tem.
 *
 * Os três são `int`: contagens MEDIDAS, e o zero de cada uma é verdade.
 */
export interface AgentVolumeInsights {
  regime: string;
  executedTaskCount: number;
  externalOriginTaskCount: number;
  delegationOriginTaskCount: number;
}

/**
 * M14 por provedor no escopo do agente.
 *
 * `searchEmbeddingInputTokens` é anulável e é a célula vazia com fonte real
 * deste bloco: nulo significa que aquele provedor não fez chamada de embedding
 * de busca na janela, nunca que fez e deu zero.
 */
export interface AgentProviderTokens {
  provider: string;
  conversationInputTokens: number | null;
  conversationOutputTokens: number | null;
  searchEmbeddingInputTokens: number | null;
}

/**
 * M11 a M17 e M19 no escopo do agente.
 *
 * **Sem `byAgent`**, e `caveats` existe aqui — o bloco `tokens` do escopo do
 * sistema não tem o campo. É por causa de `embedding-covers-search-only`.
 */
export interface AgentTokenInsights {
  conversationRegime: string;
  embeddingRegime: string;
  conversation: TokenTotals;
  /** M19 — SÓ a busca. A indexação não tem agente em fonte alguma. */
  searchEmbeddingInputTokens: number | null;
  byProvider: AgentProviderTokens[];
  /**
   * M15, M16a e M16b, e elas MUDAM DE SIGNIFICADO neste escopo: mais de uma
   * linha aqui significa que ESTE agente mudou de provedor ou de modelo dentro
   * da janela, nunca que agentes diferentes usam valores diferentes.
   *
   * Sem cache por modelo — é a lacuna L3 (#66), a mesma da página do sistema.
   */
  byModel: ModelTokens[];
  perTask: TokensPerTask;
  caveats: string[];
}

/**
 * M21 a M26 no escopo do agente.
 *
 * `maxDepthAtWhichAgentRan` NÃO é a `maxObservedDelegationDepth` do sistema com
 * um filtro — é **outra pergunta com a mesma consulta**. No sistema é o TAMANHO
 * da maior cadeia que o sistema alcançou; aqui é a POSIÇÃO mais profunda em que
 * este agente executou. Um agente que só é chamado na ponta tem profundidade
 * alta sem que isso diga nada sobre o tamanho das cadeias dele.
 *
 * O nome do campo carrega a diferença, e o rótulo na tela precisa carregá-la
 * também (D10): se algum dia for apresentado, diz "a maior profundidade em que
 * este agente executou", nunca "profundidade de delegação".
 *
 * **As quatro durações NÃO são parcelas da mesma composição**, e isso decide o
 * desenho (D5): `taskDuration` é `EndedAt − SubmittedAt` por execução,
 * `queueTime` é `StartedAt − SubmittedAt` por execução, `providerCallDuration`
 * é a média **por chamada**, e `nonProviderResidual` é por task. As populações
 * diferem — duas descontam `SubmittedAt` nulo, uma desconta `EndedAt` nulo —, e
 * é `sampleCount` que torna o desconto visível em cada uma.
 */
export interface AgentPerformanceInsights {
  regime: string;
  /** `averageMs` é MÉDIA, não mediana — não existe `percentile_cont(0.5)` na rota (L5, #66). */
  taskDuration: DurationStats;
  queueTime: DurationStats;
  /** Média por CHAMADA, não por task. O tempo de provedor por task não é servido. */
  providerCallDuration: DurationStats;
  providerCallsPerTask: number | null;
  /** O resíduo, que inclui mais do que ferramentas — daí o caveat, e daí o rótulo. */
  nonProviderResidual: DurationStats;
  maxDepthAtWhichAgentRan: number | null;
  caveats: string[];
}

/** M28 sem o agrupamento por agente. Provedor e modelo são anuláveis na fonte. */
export interface AgentModelFailures {
  provider: string | null;
  model: string | null;
  failedCount: number;
}

/**
 * M29 — um valor do vocabulário fechado de motivo de recusa e a contagem dele.
 *
 * Declarada AQUI e não em `systemInsights.ts` porque a página do sistema ainda
 * não conhece estes campos: `apps/api` passou a servi-los na change
 * `recusa-motivo-coleta` (#51) e o painel não acompanhou. É achado com issue
 * própria; quando ela fechar, esta interface sobe para o módulo neutro.
 */
export interface RejectionReasonCount {
  reason: string;
  count: number;
}

/**
 * M27 a M29 e M32 no escopo do agente.
 *
 * **TRÊS POPULAÇÕES QUE NÃO SE SOMAM**, e o campo separado é o que impede a
 * soma:
 *
 *   - `failedCount` — execuções que falharam. É o numerador do percentual;
 *   - `rejectedCount` — recusa COM linha de execução, contada pelas tabelas de
 *     métrica. Hoje só a de profundidade de delegação, feita por
 *     `apps/workers`. Fica fora do percentual de falha, no numerador e no
 *     denominador;
 *   - `rejectedAtEntryCount` — recusa ANTES de qualquer execução, com **regime
 *     de medição próprio** (`rejectionRegime`). Somar com a de cima juntaria
 *     duas janelas de regime diferente num rótulo só.
 *
 * **Sem `indexingFailures`** e **sem `byAgent`**, pelas razões do cabeçalho.
 *
 * Note que o campo do regime de execução se chama `regime` aqui, e
 * `executionRegime` no gêmeo do sistema. Conferido no corpo real — não
 * uniformizar de memória.
 */
export interface AgentErrorInsights {
  regime: string;
  rejectionRegime: string;
  failedCount: number;
  rejectedCount: number;
  rejectedAtEntryCount: number;
  byProviderAndModel: AgentModelFailures[];
  byPhase: FailurePhaseCount[];
  rejectionsByReason: RejectionReasonCount[];
  nonTerminal: NonTerminalTasks;
  caveats: string[];
}

/**
 * O que o agente TENTOU delegar, por destino **e por resultado**.
 *
 * A rota devolve uma linha por par destino × resultado, e o `outcome` é
 * discriminado porque é ele que explica a divergência com o outro lado:
 * `NotStarted` não cria task alguma no destino, e `Expired` pode ter criado uma
 * que nunca rodou. Colapsar num total por destino joga fora a causa.
 *
 * Só o identificador: **o nome não vem nesta rota**, e sai do catálogo.
 */
export interface DelegatesToRow {
  targetAgentId: string;
  outcome: string;
  count: number;
}

/** O que de fato RODOU no agente por delegação, por agente de origem. */
export interface TriggeredByRow {
  sourceAgentId: string;
  executedCount: number;
}

/**
 * M34, e a decisão central desta tela.
 *
 * ------------------------------------------------------------------------
 * OS DOIS LADOS NÃO SÃO ESPELHO, E DIVERGIR É RESULTADO CORRETO.
 * ------------------------------------------------------------------------
 *
 * `delegatesTo` é o que o agente TENTOU (`delegation_outcomes` pelo agente de
 * ORIGEM); `triggeredBy` é o que de fato RODOU nele (`task_executions` com
 * origem de delegação, pelo agente de DESTINO). São perguntas diferentes sobre
 * a mesma relação, e **podem mostrar números diferentes** para os mesmos dois
 * agentes. Duas causas, independentes:
 *
 *   1. resultado que não produz execução — `NotStarted` nunca cria task, e
 *      `Expired` pode ter criado uma que nunca rodou;
 *   2. **dois relógios** — "delega para" é situado pela execução de ORIGEM (a
 *      janela vem do pai, porque `delegation_outcomes` não tem coluna temporal
 *      utilizável), e "acionado por" pela de DESTINO. Uma delegação às 23:58
 *      cujo destino roda às 00:03 cai em dias diferentes do balde.
 *
 * **A segunda não some com período maior.** Ela é estrutural, não ruído de
 * borda a arredondar.
 *
 * A tela NÃO deriva um do outro, NÃO exibe total que some os dois e NÃO
 * sinaliza a diferença como erro — ver `delegation-sides-are-not-mirrors`.
 */
export interface AgentDelegationInsights {
  regime: string;
  delegatesTo: DelegatesToRow[];
  triggeredBy: TriggeredByRow[];
  caveats: string[];
}

/**
 * O corpo de `GET /insights/agents/{id}`.
 *
 * `caveats` existe em **quatro** blocos — `tokens` (1), `performance` (2),
 * `errors` (2) e `delegation` (1) —, contra dois na página do sistema.
 * Conferido nos handlers e no corpo real. São seis códigos, e a POSIÇÃO de cada
 * um é por superfície, não herdada da página do sistema (D11).
 */
export interface AgentInsights {
  agentId: string;
  window: InsightsWindow;
  /** Mapa nome → instante ISO. Três hoje: `execution`, `embedding`, `rejection`. */
  regimes: Record<string, string>;
  volume: AgentVolumeInsights;
  temporal: TemporalInsights;
  tokens: AgentTokenInsights;
  performance: AgentPerformanceInsights;
  errors: AgentErrorInsights;
  delegation: AgentDelegationInsights;
}
