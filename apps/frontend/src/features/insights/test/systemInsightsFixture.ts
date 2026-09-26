import type {
  DelegationInsights,
  ErrorInsights,
  InsightsWindow,
  PerformanceInsights,
  SystemInsights,
  TemporalInsights,
  TokenInsights,
  VolumeInsights,
} from '../types/systemInsights';

// O DUPLO ÚNICO DESTA CHANGE (projeção da convenção 18, e ele vai em linha
// própria na medição).
//
// É o que torna barato escrever cinquenta cenários que diferem num campo cada:
// sem ele, cada teste que quer afirmar "`tokenCount` nulo não vira 0" teria de
// montar as oito seções da resposta à mão, e a diferença que o teste existe para
// provar ficaria enterrada em oitenta linhas de ruído.
//
// A SOBRESCRITA É POR BLOCO, e é de propósito: `systemInsights({ temporal: {…} })`
// substitui o bloco temporal INTEIRO em vez de mesclar campo a campo. Mesclagem
// profunda esconderia o que o teste está dizendo — quem lê o caso precisa ver o
// bloco completo que ele afirma, não um fragmento sobre um default invisível.
// Por isso cada bloco tem o seu próprio construtor abaixo, com defaults que
// podem ser sobrescritos campo a campo NAQUELE nível.
//
// OS DEFAULTS SÃO ZEROS E NULOS MEDIDOS, não números plausíveis: um teste que
// esqueça de declarar o campo que lhe interessa falha em vez de passar por
// acidente sobre um valor inventado.

export function insightsWindowFixture(
  overrides: Partial<InsightsWindow> = {},
): InsightsWindow {
  return {
    from: '2026-09-15T03:00:00+00:00',
    to: '2026-09-25T02:59:59+00:00',
    timeZone: 'America/Sao_Paulo',
    ...overrides,
  };
}

export function volumeFixture(overrides: Partial<VolumeInsights> = {}): VolumeInsights {
  return {
    regime: 'execution',
    executedTaskCount: 0,
    externalOriginTaskCount: 0,
    ...overrides,
  };
}

export function temporalFixture(overrides: Partial<TemporalInsights> = {}): TemporalInsights {
  return {
    regime: 'execution',
    dailySeries: [],
    byWeekday: [],
    peakWeekday: null,
    ...overrides,
  };
}

export function tokensFixture(overrides: Partial<TokenInsights> = {}): TokenInsights {
  return {
    conversationRegime: 'execution',
    embeddingRegime: 'embedding',
    conversation: { inputTokens: null, outputTokens: null, cachedInputTokens: null },
    embeddingInputTokens: null,
    byAgent: [],
    byProvider: [],
    byModel: [],
    perTask: { average: null, p95: null },
    ...overrides,
  };
}

const emptyStats = { averageMs: null, p95Ms: null, sampleCount: 0 };

export function performanceFixture(
  overrides: Partial<PerformanceInsights> = {},
): PerformanceInsights {
  return {
    regime: 'execution',
    taskDuration: { ...emptyStats },
    queueTime: { ...emptyStats },
    providerCallDuration: { ...emptyStats },
    providerCallsPerTask: null,
    nonProviderResidual: { ...emptyStats },
    maxObservedDelegationDepth: null,
    // Os dois códigos que a rota real serve neste bloco, conferidos em 24/09.
    caveats: ['submitted-at-missing-on-redelivery', 'residual-is-not-only-tools'],
    ...overrides,
  };
}

export function errorsFixture(overrides: Partial<ErrorInsights> = {}): ErrorInsights {
  return {
    executionRegime: 'execution',
    indexingRegime: 'embedding',
    failedCount: 0,
    rejectedCount: 0,
    byAgent: [],
    byPhase: [],
    indexingFailures: [],
    nonTerminal: { openExecutionCount: 0, neverConsumedCount: 0, observedStates: [] },
    // DOIS códigos, não três. Eram três até a change `recusa-motivo-coleta`
    // (#51): `rejection-reason-not-collected` saiu do handler quando o motivo
    // passou a ter fonte, e esta fixture ficou para trás. Reconferido contra o
    // corpo real em 26/09 — `GET /insights/system` serve exatamente estes dois.
    caveats: ['rejections-missing-from-executions', 'point-in-time-only'],
    ...overrides,
  };
}

export function delegationFixture(
  overrides: Partial<DelegationInsights> = {},
): DelegationInsights {
  return { regime: 'execution', pairs: [], ...overrides };
}

/** Os dois regimes que a rota declara hoje. A tela não depende de serem dois. */
export const REGIMES_FIXTURE: Record<string, string> = {
  execution: '2026-09-22T01:21:00-03:00',
  embedding: '2026-09-23T01:18:00-03:00',
};

export function systemInsightsFixture(
  overrides: Partial<SystemInsights> = {},
): SystemInsights {
  return {
    window: insightsWindowFixture(),
    regimes: { ...REGIMES_FIXTURE },
    volume: volumeFixture(),
    temporal: temporalFixture(),
    tokens: tokensFixture(),
    performance: performanceFixture(),
    errors: errorsFixture(),
    delegation: delegationFixture(),
    ...overrides,
  };
}
