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

/** Só tokens. Sem nome, sem tasks, sem duração — é a lacuna L4 (#67). */
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

export interface ErrorInsights {
  executionRegime: string;
  indexingRegime: string;
  failedCount: number;
  /** Sem motivo — é a lacuna L1 (#51). */
  rejectedCount: number;
  byAgent: AgentFailures[];
  byPhase: FailurePhaseCount[];
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
 * (3) —, e não em todos. Conferido no corpo real e no `SystemInsightsResponse.cs`
 * em 23/09, reconferido em 24/09. Os cinco códigos chegam por esses dois, e a
 * tela precisa saber a qual NÚMERO cada um se aplica, porque o bloco que o
 * carrega não é o único que ele limita (D12).
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
