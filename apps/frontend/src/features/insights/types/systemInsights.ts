// O CONTRATO DE `GET /insights/system`, TRANSCRITO DE
// `apps/api/src/Buteco.Api/Insights/Responses/SystemInsightsResponse.cs` E
// CONFERIDO CONTRA O CORPO REAL (design.md, "O que foi lido da rota real" e a
// releitura de 24/09).
//
// TODO CAMPO ANULÁVEL ENTRA COMO `| null`, E NENHUM RECEBE DEFAULT. Esta é a
// primeira das três linhas de defesa da gramática dos quatro estados: um
// `number` com `?? 0` aqui apagaria a distinção entre "não reportado" e
// "reportou zero" antes de qualquer componente ver o dado, e nenhum teste de
// apresentação conseguiria pegá-la depois. As outras duas são `metricState.ts`
// e as asserções negativas dos componentes.
//
// O QUE É ANULÁVEL E O QUE NÃO É VEM DO C#, NÃO DE PALPITE:
//   - `long?`/`double?`/`int?` → `number | null`
//   - `int` (contagem medida) → `number`, e o zero dele é VERDADE
//   - `DayOfWeek?` → `number | null` (serializa como inteiro, 0 = domingo)
//   - `DateOnly` → `string` no formato `YYYY-MM-DD`
//
// A diferença entre `int` e `long?` é o contrato inteiro em miniatura: contagem
// é feita e pode dar zero; soma de tokens é nula quando ninguém reportou.

/** `InsightsWindowResponse` — os limites ecoados e o fuso que decide o dia. */
export interface InsightsWindow {
  from: string;
  to: string;
  /** O fuso do servidor. TODA aritmética de dia usa este, nunca o do navegador. */
  timeZone: string;
}

/**
 * `TemporalInsightsResponse.DailySeries` — um ponto por dia MEDIDO.
 *
 * A ausência de um dia significa uma coisa só: ele não foi medido. A rota emite
 * ponto para o dia medido e vazio (`taskCount: 0`) e omite o dia anterior ao
 * início do regime — corrigido pela #65, conferido contra a rota em 24/09.
 *
 * `tokenCount` é nulo no dia medido e vazio: "foram zero tasks" e "não há token
 * a relatar" são afirmações diferentes, e o `0` de uma não vaza para a outra.
 */
export interface DailyInsightPoint {
  /** `YYYY-MM-DD` no fuso da resposta. */
  day: string;
  taskCount: number;
  tokenCount: number | null;
}

/** 0 = domingo, mesmo mapeamento de `DayOfWeek`. */
export interface WeekdayInsightPoint {
  weekday: number;
  taskCount: number;
}

export interface VolumeInsights {
  regime: string;
  executedTaskCount: number;
  externalOriginTaskCount: number;
}

export interface TemporalInsights {
  regime: string;
  dailySeries: DailyInsightPoint[];
  byWeekday: WeekdayInsightPoint[];
  /** Nulo quando não há ocorrência — sem medição não há pico. */
  peakWeekday: number | null;
}

export interface TokenTotals {
  inputTokens: number | null;
  outputTokens: number | null;
  cachedInputTokens: number | null;
}

/**
 * Só tokens, por agente. Sem nome — ele vem do catálogo — e sem tasks, tokens
 * por task ou duração: essas três **não são lacuna**, são ausência decidida
 * (#67, fechada). Elas têm fonte em `GET /insights/agents/{id}` e aparecem na
 * aba de Insights do agente, que é para onde o nome desta tabela leva.
 */
export interface AgentTokens {
  agentId: string;
  inputTokens: number | null;
  outputTokens: number | null;
}

export interface ProviderTokens {
  provider: string;
  conversationInputTokens: number | null;
  conversationOutputTokens: number | null;
  embeddingInputTokens: number | null;
}

/** Sem cache por modelo — é a lacuna L3 (#66). `callCount` é contagem medida. */
export interface ModelTokens {
  provider: string;
  model: string;
  totalTokens: number | null;
  callCount: number;
}

export interface TokensPerTask {
  average: number | null;
  p95: number | null;
}

export interface TokenInsights {
  conversationRegime: string;
  embeddingRegime: string;
  conversation: TokenTotals;
  embeddingInputTokens: number | null;
  byAgent: AgentTokens[];
  byProvider: ProviderTokens[];
  byModel: ModelTokens[];
  perTask: TokensPerTask;
}

/**
 * `sampleCount` é quantas execuções ENTRARAM no cálculo, não quantas existem —
 * é o desconto de `SubmittedAt` nulo em reentrega, tornado visível de propósito.
 */
export interface DurationStats {
  averageMs: number | null;
  p95Ms: number | null;
  sampleCount: number;
}

export interface PerformanceInsights {
  regime: string;
  /** `averageMs` é MÉDIA, não mediana — não existe `percentile_cont(0.5)` na rota (L5). */
  taskDuration: DurationStats;
  queueTime: DurationStats;
  providerCallDuration: DurationStats;
  providerCallsPerTask: number | null;
  nonProviderResidual: DurationStats;
  maxObservedDelegationDepth: number | null;
  caveats: string[];
}

export interface AgentFailures {
  agentId: string;
  provider: string | null;
  model: string | null;
  failedCount: number;
}

export interface FailurePhaseCount {
  phase: string;
  count: number;
}

/**
 * M29 — um valor do vocabulário fechado de motivo de recusa e a contagem dele na
 * janela.
 *
 * **MESMO FORMATO de `FailurePhaseCount`, e a rota escolheu assim de propósito**
 * — lista rasa, rótulo e contagem, sem subcardinalidade —, lendo o que o card de
 * Motivos já fazia. Mas **lista própria, e não dentro de `byPhase`**: o
 * vocabulário de `byPhase` é o `FailurePhase` de `task_executions`, que a
 * capability `agent-execution-metrics` enumera em sete valores. Um motivo de
 * recusa ali seria mentir no contrato de outra capability.
 *
 * **Ela MUDOU DE ARQUIVO nesta change, e o gatilho estava escrito.** A #53 a
 * declarou em `agentInsights.ts` com a razão e a condição de volta: *"Declarada
 * AQUI e não em `systemInsights.ts` porque a página do sistema ainda não conhece
 * estes campos… quando ela fechar, esta interface sobe para o módulo neutro."* A
 * issue é a #75, e é esta change. `agentInsights.ts` passa a importá-la daqui e a
 * reexportá-la, como já faz com as outras sete formas neutras de escopo.
 *
 * **A soma das contagens fecha com `rejectedAtEntryCount`** da mesma janela,
 * porque a coluna de motivo é obrigatória na fonte. Um valor que a tela não
 * conheça é apresentado COMO ESTÁ: omiti-lo faria a soma deixar de fechar, sem
 * sintoma.
 */
export interface RejectionReasonCount {
  reason: string;
  count: number;
}

export interface IndexingFailure {
  outcome: string;
  failurePhase: string | null;
  count: number;
}

export interface NonTerminalTasks {
  openExecutionCount: number;
  neverConsumedCount: number;
  observedStates: string[];
}

/**
 * M27 a M30 e M32.
 *
 * **TRÊS REGIMES DE MEDIÇÃO NUM BLOCO SÓ**, e nenhum eleito para representá-lo: o
 * handler lê `task_executions` (execução), `knowledge_indexing_attempts`
 * (embedding) e `task_rejections` (recusa). É por isso que há três campos de
 * regime e não um.
 *
 * **E DUAS POPULAÇÕES DE RECUSA QUE NÃO SE SOMAM.** O campo separado é o que
 * impede a soma:
 *
 *   - `rejectedCount` — recusa **com** linha de execução, derivada das tabelas de
 *     métrica. Hoje só a de profundidade de delegação, feita por `apps/workers`
 *     (medido: `AgentExecutionService.cs:236` é o único sítio que grava
 *     `TerminalState = 'Rejected'`). Regime de **execução**;
 *   - `rejectedAtEntryCount` — recusa **antes** de qualquer execução, feita por
 *     `apps/api`. Regime **próprio** (`rejectionRegime`). Somá-la com a de cima
 *     juntaria duas janelas de regimes diferentes num rótulo só.
 *
 * As duas ficam fora do percentual de falha, no numerador e no denominador, e é
 * isso que `rejections-missing-from-executions` declara.
 */
export interface ErrorInsights {
  executionRegime: string;
  indexingRegime: string;
  /**
   * O regime de `task_rejections`. **Terceiro do mapa `regimes`**, e o primeiro
   * que obriga a tela a rotular regime por nome em vez de por ternário.
   */
  rejectionRegime: string;
  failedCount: number;
  /**
   * Recusa **com** linha de execução — ver o cabeçalho do bloco.
   *
   * *(Dizia "Sem motivo — é a lacuna L1 (#51)". Duas coisas erradas, e a correção
   * vai com a causa, convenção 9: a L1 **fechou** com a #51, que passou a servir o
   * motivo em `rejectionsByReason`; e o motivo que ela coletou não é desta
   * contagem — é da recusa de entrada. Este campo nunca teve motivo porque é
   * outra população, não porque a fonte faltava.)*
   *
   * **Não é apresentada na página do sistema**: o `Main.dc.html` não tem elemento
   * para ela — não há KPI de taxa de falha nesta tela —, e o rótulo "Recusadas na
   * entrada" que ela ocupava é da outra população. *Servido e não desenhado*, com
   * issue e gatilho (#75, design.md D3).
   */
  rejectedCount: number;
  /** Recusa de ENTRADA. Contagem medida, e o zero dela é verdade. */
  rejectedAtEntryCount: number;
  byAgent: AgentFailures[];
  byPhase: FailurePhaseCount[];
  /** Ordenada por contagem pela rota. A soma fecha com `rejectedAtEntryCount`. */
  rejectionsByReason: RejectionReasonCount[];
  indexingFailures: IndexingFailure[];
  nonTerminal: NonTerminalTasks;
  caveats: string[];
}

export interface DelegationPair {
  sourceAgentId: string;
  targetAgentId: string;
  outcome: string;
  count: number;
}

export interface DelegationInsights {
  regime: string;
  pairs: DelegationPair[];
}

/**
 * O corpo de `GET /insights/system`.
 *
 * `caveats` existe em DOIS blocos só — `performance` (2 códigos) e `errors`
 * (2) —, e não em todos. **QUATRO códigos** chegam por esses dois, e a tela
 * precisa saber a qual NÚMERO cada um se aplica, porque o bloco que o carrega não
 * é o único que ele limita (D12).
 *
 * **Critério de contagem, para quem recontar** (convenção 22 — o número vai com o
 * critério, não só com o estado): os literais `InsightsCaveats.*` passados a
 * construtor de resposta em `GetSystemInsightsQueryHandler.cs`, que são `:529`
 * (`performance`, dois) e `:698-699` (`errors`, dois). Recontado em 27/09/2026
 * sobre a `main` em `8f646c0`.
 *
 * *(Dizia `errors` **(3)** e "os cinco códigos", conferido em 23/09 e reconferido
 * em 24/09. **Estava certo nas duas datas** e envelheceu com a #51, que tirou
 * `rejection-reason-not-collected` dos dois handlers. E ESTE É O SÍTIO DE ORIGEM
 * DO `5` QUE `caveatLabels.ts` carregou errado: lá a decomposição foi recalibrada
 * para (2) e (2) e **o total continuou 5**, somando 4. É a convenção 22 na forma
 * mais barata de acontecer — recalibrar as partes e citar o headline antigo — e a
 * 18 dizendo por que headline não serve: só a decomposição é verificável.)*
 */
export interface SystemInsights {
  window: InsightsWindow;
  /** Mapa nome → instante ISO. A tela não depende de quantos nem de quais. */
  regimes: Record<string, string>;
  volume: VolumeInsights;
  temporal: TemporalInsights;
  tokens: TokenInsights;
  performance: PerformanceInsights;
  errors: ErrorInsights;
  delegation: DelegationInsights;
}
