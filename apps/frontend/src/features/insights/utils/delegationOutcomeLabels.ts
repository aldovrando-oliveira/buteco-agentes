// O RESULTADO DA DELEGAÇÃO → RÓTULO DE OPERADOR.
//
// Os quatro valores vêm de `ExecutionMetricsValues.DelegationOutcome`
// (`apps/workers`), lidos do código em 26/09 — não de memória. Vocabulário
// FECHADO gravado como texto, nunca como ordinal (convenção 12).
//
// ---------------------------------------------------------------------------
// POR QUE O RESULTADO PRECISA APARECER NA TELA, E NÃO SÓ O TOTAL
// ---------------------------------------------------------------------------
//
// É ele que explica a divergência entre os dois lados da delegação. Sem ver que
// houve `NotStarted`, o operador lê dois números diferentes para a mesma
// relação e conclui defeito. Com o resultado à vista, lê a causa:
//
//   - `NotStarted`        — NUNCA cria task no destino. A delegação foi
//                           registrada e o outro lado não tem o que contar;
//   - `Expired`           — pode ter criado uma task que nunca rodou, e aí
//                           também não há execução do outro lado;
//   - `TargetUnsuccessful` — o destino rodou e não concluiu: há execução do
//                           outro lado, e os dois lados contam;
//   - `Completed`         — rodou e concluiu.
//
// Os dois primeiros são a **primeira** das duas causas da assimetria. A segunda
// — os dois relógios — não tem valor de vocabulário nenhum, e é por isso que o
// `caveat` continua sendo necessário ao lado disto.
//
// O DESCONHECIDO É APRESENTADO CRU, pelo mesmo raciocínio de
// `failurePhaseLabels.ts`: omitir faria a soma dos resultados deixar de fechar
// com o total do destino, sem sintoma; cair no rótulo de outro resultado faria
// a tela AFIRMAR uma causa errada, que é pior que não afirmar nenhuma.

const DELEGATION_OUTCOME_LABELS: Record<string, string> = {
  Completed: 'Concluída',
  TargetUnsuccessful: 'Destino não concluiu',
  Expired: 'Expirou',
  NotStarted: 'Não iniciada',
};

export interface OutcomeLabel {
  text: string;
  /** `true` quando a tela não conhece o valor e o está exibindo cru. */
  unknown: boolean;
}

export function delegationOutcomeLabel(outcome: string): OutcomeLabel {
  const known = DELEGATION_OUTCOME_LABELS[outcome];
  return known === undefined
    ? { text: outcome, unknown: true }
    : { text: known, unknown: false };
}

/** Exportado para o teste afirmar que o vocabulário é o fechado dos quatro. */
export const KNOWN_DELEGATION_OUTCOMES = Object.keys(DELEGATION_OUTCOME_LABELS);
