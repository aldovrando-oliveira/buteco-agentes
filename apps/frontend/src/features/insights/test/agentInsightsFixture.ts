import type {
  AgentDelegationInsights,
  AgentErrorInsights,
  AgentInsights,
  AgentPerformanceInsights,
  AgentTokenInsights,
  AgentVolumeInsights,
} from '../types/agentInsights';
import { insightsWindowFixture, temporalFixture } from './systemInsightsFixture';

// O DUPLO ÚNICO DESTA CHANGE — e na medição da convenção 18 ele é **arranjo
// compartilhado**, não caso. Foi a categoria que a vigésima medição descobriu:
// semeadura de fixture custa linhas que a tabela de casos não contava.
//
// A SOBRESCRITA É POR BLOCO, pelo mesmo motivo do gêmeo do sistema: quem lê o
// caso precisa ver o bloco completo que ele afirma, não um fragmento sobre um
// default invisível.
//
// OS DEFAULTS SÃO ZEROS E NULOS MEDIDOS, nunca números plausíveis: um teste que
// esqueça de declarar o campo que lhe interessa falha em vez de passar por
// acidente.
//
// `insightsWindowFixture` e `temporalFixture` são IMPORTADOS do duplo do
// sistema, e não recriados: as duas formas são neutras de escopo, iguais nas
// duas rotas (D16). Duplicá-las criaria dois lugares onde o mesmo conceito pode
// divergir.

export const AGENT_ID = '318924ed-78da-41ff-9536-bd4f372fc0c3';
/** O destino nos cenários de saída — "Cobrança" no protótipo. */
export const TARGET_ID = '475a81a2-0106-49e3-b612-4f17305c7c7a';
/** A origem nos cenários de entrada — "Triagem" no protótipo. */
export const SOURCE_ID = '9e873fc1-2769-450d-b068-3953958a9aa5';

export function agentVolumeFixture(
  overrides: Partial<AgentVolumeInsights> = {},
): AgentVolumeInsights {
  return {
    regime: 'execution',
    executedTaskCount: 0,
    externalOriginTaskCount: 0,
    delegationOriginTaskCount: 0,
    ...overrides,
  };
}

export function agentTokensFixture(
  overrides: Partial<AgentTokenInsights> = {},
): AgentTokenInsights {
  return {
    conversationRegime: 'execution',
    embeddingRegime: 'embedding',
    conversation: { inputTokens: null, outputTokens: null, cachedInputTokens: null },
    searchEmbeddingInputTokens: null,
    byProvider: [],
    byModel: [],
    perTask: { average: null, p95: null },
    // O código que a rota real serve neste bloco, conferido em 26/09 contra o
    // HEAD. O bloco `tokens` do escopo do SISTEMA não tem `caveats`.
    caveats: ['embedding-covers-search-only'],
    ...overrides,
  };
}

const emptyStats = { averageMs: null, p95Ms: null, sampleCount: 0 };

export function agentPerformanceFixture(
  overrides: Partial<AgentPerformanceInsights> = {},
): AgentPerformanceInsights {
  return {
    regime: 'execution',
    taskDuration: { ...emptyStats },
    queueTime: { ...emptyStats },
    providerCallDuration: { ...emptyStats },
    providerCallsPerTask: null,
    nonProviderResidual: { ...emptyStats },
    maxDepthAtWhichAgentRan: null,
    caveats: ['submitted-at-missing-on-redelivery', 'residual-is-not-only-tools'],
    ...overrides,
  };
}

export function agentErrorsFixture(
  overrides: Partial<AgentErrorInsights> = {},
): AgentErrorInsights {
  return {
    regime: 'execution',
    rejectionRegime: 'rejection',
    failedCount: 0,
    rejectedCount: 0,
    rejectedAtEntryCount: 0,
    byProviderAndModel: [],
    byPhase: [],
    rejectionsByReason: [],
    nonTerminal: { openExecutionCount: 0, neverConsumedCount: 0, observedStates: [] },
    // DOIS códigos, não três: `rejection-reason-not-collected` caiu com a #51 e
    // não chega em nenhum dos dois escopos. Conferido no corpo real em 26/09.
    caveats: ['rejections-missing-from-executions', 'point-in-time-only'],
    ...overrides,
  };
}

export function agentDelegationFixture(
  overrides: Partial<AgentDelegationInsights> = {},
): AgentDelegationInsights {
  return {
    regime: 'execution',
    delegatesTo: [],
    triggeredBy: [],
    caveats: ['delegation-sides-are-not-mirrors'],
    ...overrides,
  };
}

/** Os TRÊS regimes que a rota do agente declara. Conferido no corpo real. */
export const AGENT_REGIMES_FIXTURE: Record<string, string> = {
  execution: '2026-09-22T01:21:00-03:00',
  embedding: '2026-09-23T01:18:00-03:00',
  rejection: '2026-09-25T00:00:00-03:00',
};

export function agentInsightsFixture(
  overrides: Partial<AgentInsights> = {},
): AgentInsights {
  return {
    agentId: AGENT_ID,
    window: insightsWindowFixture(),
    regimes: { ...AGENT_REGIMES_FIXTURE },
    volume: agentVolumeFixture(),
    temporal: temporalFixture(),
    tokens: agentTokensFixture(),
    performance: agentPerformanceFixture(),
    errors: agentErrorsFixture(),
    delegation: agentDelegationFixture(),
    ...overrides,
  };
}

// --------------------------------------------------------- OS QUATRO CENÁRIOS
//
// A nota `cenarios` do `canvas.json` é explícita: "uma estrutura, três
// preenchimentos" — e o QUARTO, que não tem artboard, é "o mesmo card com as
// duas seções tracejadas".
//
// Estes construtores existem para que cada teste declare o CENÁRIO e não o
// arranjo. Sem eles, os quatro casos do guarda dos cenários custariam quarenta
// linhas de semeadura cada.

/** Cenário 1 — só delega. `Agente-Insights.dc.html`. */
export function delegatesOnlyFixture(): AgentDelegationInsights {
  return agentDelegationFixture({
    delegatesTo: [{ targetAgentId: TARGET_ID, outcome: 'Completed', count: 27 }],
    triggeredBy: [],
  });
}

/** Cenário 2 — só é delegado. `Agente-Delegado.dc.html`. */
export function triggeredOnlyFixture(): AgentDelegationInsights {
  return agentDelegationFixture({
    delegatesTo: [],
    triggeredBy: [{ sourceAgentId: SOURCE_ID, executedCount: 51 }],
  });
}

/** Cenário 3 — os dois lados. `Agente-Misto.dc.html`. */
export function mixedDelegationFixture(): AgentDelegationInsights {
  return agentDelegationFixture({
    delegatesTo: [{ targetAgentId: TARGET_ID, outcome: 'Completed', count: 51 }],
    triggeredBy: [{ sourceAgentId: SOURCE_ID, executedCount: 70 }],
  });
}

/** Cenário 4 — nenhum dos dois. SEM artboard; vem da nota `cenarios`. */
export function noDelegationFixture(): AgentDelegationInsights {
  return agentDelegationFixture({ delegatesTo: [], triggeredBy: [] });
}

/**
 * O CENÁRIO QUE MAIS IMPORTA: os dois lados DIVERGEM para a mesma relação.
 *
 * O agente tentou 30 delegações para o mesmo destino — 24 concluídas, 6 nunca
 * iniciadas — e o destino, olhando do lado dele, executou 24. **Os números
 * discordam, e discordar é resultado correto.** `NotStarted` não cria task
 * alguma no destino.
 *
 * O guarda que usa esta semente AFIRMA a divergência. Um guarda que afirmasse
 * igualdade reprovaria o comportamento correto, e é por isso que a semente tem
 * de existir de propósito: com números que batem, o guarda ficaria verde com e
 * sem o defeito (convenção 15, primeira forma).
 */
export function divergentDelegationFixture(): AgentDelegationInsights {
  return agentDelegationFixture({
    delegatesTo: [
      { targetAgentId: TARGET_ID, outcome: 'Completed', count: 24 },
      { targetAgentId: TARGET_ID, outcome: 'NotStarted', count: 6 },
    ],
    triggeredBy: [{ sourceAgentId: TARGET_ID, executedCount: 24 }],
  });
}
