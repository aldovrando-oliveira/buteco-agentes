// VOCABULÁRIO DE MOTIVO DE RECUSA → RÓTULO DE OPERADOR.
//
// Os quatro valores vêm de `RejectionMetricsValues.Reason` (`apps/api`), lidos do
// código em 27/09/2026 — não de memória. São vocabulário FECHADO gravado em
// `task_rejections` como texto, nunca como ordinal (convenção 12), e é por isso
// que a tela pode mapeá-los por string.
//
// ===========================================================================
// POR QUE ESTE MÓDULO NASCE AQUI, E POR QUE ISSO NÃO É ABSTRAÇÃO PREMATURA
// ===========================================================================
//
// A #80 dizia que ele já existia: *"o vocabulário já está traduzido em
// `utils/rejectionReasonLabels.ts`, escrito pela #53 — este seria o segundo
// consumidor dele, e nenhum código novo é necessário para isso"*. **O arquivo
// nunca existiu.** A change `insights-aba-do-agente` (#53) REMOVEU a recusa de
// entrada e os motivos do `AgentFailuresCard` em vez de traduzi-los, por decisão
// do dono na tela: a D6 e a D10 daquela change se contradiziam, e o artboard do
// agente não tem elemento para eles (ver `AgentFailuresCard.tsx`).
//
// Então a página do sistema é a PRIMEIRA superfície a traduzir este vocabulário,
// não a segunda, e **o gatilho da convenção 2 não foi atingido** — há um
// consumidor, e a régua pede dois ou três REAIS.
//
// **Isso não significa "não criar o módulo".** A convenção 2 governa EXTRAIR
// código compartilhado a partir de repetição já observada; ela não manda escrever
// a primeira cópia dentro do consumidor. O que este arquivo é: o lugar onde o
// código mora. O que ele NÃO é: extração de repetição.
//
// ===========================================================================
// POR QUE NÃO DENTRO DE `failurePhaseLabels.ts`
// ===========================================================================
//
// Aquele módulo já hospeda TRÊS dicionários fechados — fases de execução,
// resultados de indexação, fases de indexação — e já exporta o tipo `PhaseLabel`
// com o contrato `unknown` de que a régua do desconhecido precisa. Somar o quarto
// ali seria o caminho mais curto.
//
// **Recusado, e o motivo é o miolo da change:** motivo de recusa NÃO é fase de
// falha. A recusa não produz linha de execução; a falha é execução que começou e
// quebrou. A change inteira existe para impedir que as duas populações se
// confundam na tela — guardá-las no mesmo arquivo chamado `failurePhaseLabels`
// contradiria a tese no nome do módulo. É o tipo de divergência que nunca causa
// defeito e sempre causa a dúvida de se são a mesma coisa.
//
// ===========================================================================
// O QUE É COMPARTILHADO, E O QUE É COPIADO — DECLARADO
// ===========================================================================
//
// COMPARTILHADO: o tipo `PhaseLabel`, importado de `failurePhaseLabels.ts`. Já é
// exportado, e as duas superfícies precisam do mesmo par `{ text, unknown }` para
// que a apresentação neutra seja a mesma em qualquer lista.
//
// COPIADO: as três linhas de `lookup`. É a SEGUNDA ocorrência, e a convenção 2
// pede a terceira. **Gatilho de extração:** um terceiro vocabulário fechado que
// precise de `lookup` — aí os três saem para um módulo de vocabulário, e não
// antes.
//
// ===========================================================================
// O DESCONHECIDO É APRESENTADO, E AQUI ELE CUSTA MAIS DO QUE NO VIZINHO
// ===========================================================================
//
// Nas fases de falha, omitir uma fase nova faz a soma dos motivos não fechar com
// a contagem de falhas. Aqui é mais direto e mais barato de errar: **a coluna de
// motivo é obrigatória em `task_rejections`**, então a soma das contagens de
// motivo fecha EXATAMENTE com `rejectedAtEntryCount`. Omitir um valor
// desconhecido quebra a soma sem nenhum sintoma na tela — ninguém soma à mão.
//
// O vocabulário cresce em `apps/api` sem esta tela saber. Um valor novo chega como
// o próprio texto, visível, e quem o vir sabe que falta traduzir.

import type { PhaseLabel } from './failurePhaseLabels';

/**
 * Os QUATRO motivos de `RejectionMetricsValues.Reason`.
 *
 * O rótulo de `ProviderOrModelMissing` é o do próprio protótipo — o
 * `Main.dc.html` escreve *"Agente sem provider ou modelo configurado"* na linha de
 * recusa do card de Motivos. Os outros três o artboard não desenha, porque ele foi
 * feito quando a recusa era um número sem motivo.
 *
 * `AgentNotFound` e `AgentInactive` são separados de propósito, e a distinção é de
 * `apps/api`: o sítio de agente inativo carregava as duas causas porque
 * `FirstOrDefaultAsync` sobre um `record struct` devolvia `default` — com
 * `IsActive = false` — quando o agente não existia. **Para quem opera são
 * problemas diferentes:** um se resolve reativando o agente, o outro significa que
 * o cliente está chamando o endereço A2A de um agente que não está lá.
 */
const REJECTION_REASON_LABELS: Record<string, string> = {
  AgentNotFound: 'Agente não encontrado',
  AgentInactive: 'Agente inativo',
  ProviderOrModelMissing: 'Agente sem provider ou modelo configurado',
  ProviderNotConfigured: 'Provedor sem chave configurada no ambiente',
};

function lookup(dictionary: Record<string, string>, value: string): PhaseLabel {
  const known = dictionary[value];
  return known === undefined ? { text: value, unknown: true } : { text: known, unknown: false };
}

export function rejectionReasonLabel(reason: string): PhaseLabel {
  return lookup(REJECTION_REASON_LABELS, reason);
}

/** Exportado para o teste afirmar cobertura do vocabulário inteiro. */
export const KNOWN_REJECTION_REASONS = Object.keys(REJECTION_REASON_LABELS);
