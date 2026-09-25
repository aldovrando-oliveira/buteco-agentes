// VOCABULÁRIO DE FALHA → RÓTULO DE OPERADOR.
//
// Os valores vêm de `ExecutionMetricsValues.FailurePhase` (apps/workers) e de
// `EmbeddingMetricsValues.{Outcome,FailurePhase}`, lidos do código em 24/09 —
// não de memória. São vocabulário FECHADO gravado como texto, nunca como
// ordinal (convenção 12), e é por isso que a tela pode mapeá-los por string.
//
// O DESCONHECIDO É APRESENTADO, NÃO OMITIDO NEM REAPROVEITADO.
//
// As duas alternativas erradas, e por que cada uma é pior:
//   - omitir: a fase nova some da tela, e a soma dos motivos não fecha com a
//     contagem de falhas — sem sintoma, porque ninguém soma à mão;
//   - cair no rótulo de outra fase: a tela AFIRMA uma causa errada, que é pior
//     que não afirmar nenhuma (convenção 13).
//
// O vocabulário cresce em `apps/workers` sem esta tela saber. Um valor novo
// chega como o próprio texto, visível, e quem o vir sabe que falta traduzir.

/** As sete fases de execução de `ExecutionMetricsValues.FailurePhase`. */
const EXECUTION_PHASE_LABELS: Record<string, string> = {
  DelegationDepthExceeded: 'Limite de profundidade de delegação',
  ContextLock: 'Espera pelo lock de contexto',
  ChatClientResolution: 'Resolução do cliente de conversa',
  ToolResolution: 'Resolução de ferramentas',
  SessionLoad: 'Carga da sessão',
  AgentRun: 'Execução do agente',
  Persistence: 'Gravação do resultado',
};

/** Os quatro resultados de `EmbeddingMetricsValues.Outcome`. */
const INDEXING_OUTCOME_LABELS: Record<string, string> = {
  Indexed: 'Indexado',
  RetryScheduled: 'Nova tentativa agendada',
  Failed: 'Falhou',
  Discarded: 'Descartado',
};

/** As seis fases de `EmbeddingMetricsValues.FailurePhase`. */
const INDEXING_PHASE_LABELS: Record<string, string> = {
  Chunking: 'Fragmentação',
  ProviderResolution: 'Resolução do provedor',
  EmbeddingGateway: 'Chamada ao gateway de embedding',
  VectorCountMismatch: 'Número de vetores divergente',
  DimensionMismatch: 'Dimensão do vetor divergente',
  Persistence: 'Gravação dos fragmentos',
};

export interface PhaseLabel {
  text: string;
  /** `true` quando a tela não conhece o valor e o está exibindo cru. */
  unknown: boolean;
}

function lookup(dictionary: Record<string, string>, value: string): PhaseLabel {
  const known = dictionary[value];
  return known === undefined ? { text: value, unknown: true } : { text: known, unknown: false };
}

export function executionPhaseLabel(phase: string): PhaseLabel {
  return lookup(EXECUTION_PHASE_LABELS, phase);
}

export function indexingOutcomeLabel(outcome: string): PhaseLabel {
  return lookup(INDEXING_OUTCOME_LABELS, outcome);
}

export function indexingPhaseLabel(phase: string): PhaseLabel {
  return lookup(INDEXING_PHASE_LABELS, phase);
}

/**
 * O rótulo de uma linha de falha de indexação: o resultado, mais a fase quando
 * ela existe. `failurePhase` é nulo em `Discarded`, que não tem fase.
 */
export function indexingFailureLabel(outcome: string, failurePhase: string | null): PhaseLabel {
  const outcomeLabel = indexingOutcomeLabel(outcome);
  if (failurePhase === null) {
    return outcomeLabel;
  }
  const phase = indexingPhaseLabel(failurePhase);
  return {
    text: `${outcomeLabel.text} · ${phase.text}`,
    unknown: outcomeLabel.unknown || phase.unknown,
  };
}

/** Exportado para o teste afirmar cobertura do vocabulário inteiro. */
export const KNOWN_EXECUTION_PHASES = Object.keys(EXECUTION_PHASE_LABELS);
export const KNOWN_INDEXING_OUTCOMES = Object.keys(INDEXING_OUTCOME_LABELS);
export const KNOWN_INDEXING_PHASES = Object.keys(INDEXING_PHASE_LABELS);
