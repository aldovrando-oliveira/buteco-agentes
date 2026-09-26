// OS CÓDIGOS DE PARCIALIDADE — TEXTO ÚNICO, POSIÇÃO POR SUPERFÍCIE (D11).
//
// ===========================================================================
// POR QUE O TEXTO É UM E A POSIÇÃO SÃO DUAS
// ===========================================================================
//
// O que o código diz SOBRE A MEDIÇÃO não muda de tela para tela: "execuções
// reentregues ficam fora da média" é verdade na página do sistema e na aba do
// agente. Duplicar o texto criaria dois lugares onde a mesma frase pode
// divergir (convenção 2).
//
// A POSIÇÃO muda, e não por estilo — por qual número cada tela apresenta. Os
// dois casos concretos, e são de sinais opostos:
//
//   - `residual-is-not-only-tools` NÃO tem posição na página do sistema, que
//     não desenha o resíduo, e TEM na aba do agente, que o desenha como
//     parcela de "Onde o tempo foi";
//   - `point-in-time-only` TEM posição na página do sistema, que tem o banner
//     de não-terminais, e NÃO tem na aba, que não o tem.
//
// Um mapa único de posição obrigaria uma das duas a mentir. Derivar a posição
// do BLOCO em que o código chega também não serve: o bloco que carrega o
// código não é o único que ele limita, e renderizar os códigos ao pé do bloco
// em que chegaram seria texto de limitação longe do número limitado — que é
// exatamente a forma de ninguém ler.
//
// ===========================================================================
// AS DUAS REGRAS QUE NÃO MUDAM
// ===========================================================================
//
// UM CÓDIGO SEM NÚMERO NAQUELA SUPERFÍCIE NÃO É RENDERIZADO. Um texto de
// limitação sem o número que ele limita não tem o que qualificar. Isso NÃO é
// descartar em silêncio: a classificação é `not-on-this-page`, escrita, e o dia
// em que o número entrar na tela o teste falha e aponta o lugar.
//
// CÓDIGO DESCONHECIDO APARECE CRU, como aviso visível. É a convenção da casa
// para enum desconhecido: indicador neutro, nunca silêncio, nunca reaproveitar
// o indicador de outra coisa.
//
// ===========================================================================
// O QUE A ROTA REAL SERVE — CONFERIDO EM 26/09/2026 CONTRA O `HEAD`
// ===========================================================================
//
//   escopo do sistema: 5 códigos em 2 blocos — `performance` (2), `errors` (2)
//                      … e `residual-is-not-only-tools` vem em `performance`;
//   escopo do agente : 6 códigos em 4 blocos — `tokens` (1), `performance` (2),
//                      `errors` (2), `delegation` (1).
//
// `rejection-reason-not-collected` NÃO CHEGA EM NENHUM DOS DOIS. Ele caiu dos
// dois handlers com a change `recusa-motivo-coleta` (#51), que passou a servir
// `rejectedAtEntryCount` e `rejectionsByReason` em campos próprios, e os testes
// de `apps/api` afirmam a ausência dele. A entrada morta saiu deste mapa pela
// mesma leitura — um mapa "fechado" que declara um código que a rota não emite
// deixa de ser fechado em nada. **A página do sistema ainda não consome os
// campos novos**, e isso é achado com issue própria, não escopo desta change.

/**
 * Onde, numa superfície, o texto do código é renderizado.
 *
 * A união é a de TODAS as superfícies. Uma posição que só existe numa tela é
 * inofensiva na outra, porque o mapa daquela tela simplesmente não a usa.
 */
export type CaveatPlacement =
  /** Junto da duração da task. Nas duas superfícies. */
  | 'task-duration'
  /** Junto da contagem de recusas. Nas duas superfícies. */
  | 'rejection-count'
  /** Junto do motivo da recusa. Só na página do sistema. */
  | 'rejection-reason'
  /** Junto das tasks sem estado terminal. Só na página do sistema. */
  | 'non-terminal'
  /** Junto da parcela de tempo fora do provedor. Só na aba do agente. */
  | 'residual'
  /** Dentro do card que carrega os dois lados. Só na aba do agente. */
  | 'delegation-sides'
  /** O número que ele limita não está NESTA superfície — não renderiza. */
  | 'not-on-this-page'
  /** A tela não conhece o código — renderiza cru, como aviso. */
  | 'unknown';

/** Qual tela está perguntando. O padrão é a do sistema, que veio primeiro. */
export type CaveatSurface = 'system' | 'agent';

export interface CaveatLabel {
  code: string;
  placement: CaveatPlacement;
  /** O texto de operador, ou o próprio código quando desconhecido. */
  text: string;
}

/** O mapa FECHADO de código → texto. Um só, para as duas superfícies. */
const CAVEAT_TEXTS: Record<string, string> = {
  'submitted-at-missing-on-redelivery':
    'Execuções reentregues ficam fora da média: nelas o instante de submissão não é registrado.',
  'residual-is-not-only-tools':
    'O tempo residual inclui espera de lock, chamadas MCP e busca vetorial, não só ferramentas.',
  'rejections-missing-from-executions':
    'Recusas não geram linha de execução, então não entram no percentual de falha.',
  'point-in-time-only':
    'Contagem do instante da consulta: não diz há quanto tempo as tasks estão neste estado.',
  'embedding-covers-search-only':
    'Os tokens de embedding deste agente cobrem apenas a busca: a indexação é trabalho da base, sem agente em fonte alguma.',
  'delegation-sides-are-not-mirrors':
    'Os dois lados têm fontes diferentes: um conta tentativa, o outro conta execução, e as janelas são situadas por relógios diferentes. Números distintos são resultado correto.',
};

/**
 * A posição de cada código na PÁGINA DO SISTEMA.
 *
 * `residual-is-not-only-tools` fica sem posição porque o `Main.dc.html` não
 * desenha o resíduo. Os dois códigos próprios do escopo do agente não chegam a
 * esta rota; estão aqui classificados para o caso de chegarem um dia, em vez de
 * caírem no caminho do desconhecido e virarem aviso amarelo sem causa.
 */
const SYSTEM_PLACEMENTS: Record<string, CaveatPlacement> = {
  'submitted-at-missing-on-redelivery': 'task-duration',
  'residual-is-not-only-tools': 'not-on-this-page',
  'rejections-missing-from-executions': 'rejection-count',
  'point-in-time-only': 'non-terminal',
  'embedding-covers-search-only': 'not-on-this-page',
  'delegation-sides-are-not-mirrors': 'not-on-this-page',
};

/**
 * A posição de cada código na ABA DO AGENTE, e ela é a tabela da D11.
 *
 * As três diferenças em relação à página do sistema, cada uma com a razão:
 *
 *   - `residual-is-not-only-tools` GANHA posição: a aba desenha a parcela de
 *     tempo fora do provedor, e é por causa deste código que ela não se chama
 *     "Ferramentas";
 *   - `point-in-time-only` PERDE: a aba não tem o banner de não-terminais, que
 *     o artboard não desenha;
 *   - `embedding-covers-search-only` não tem posição: nenhum artboard do agente
 *     tem card de embedding nem de provedor. Classificado, não esquecido — e o
 *     dia em que ganhar elemento, o teste falha e aponta o lugar.
 */
const AGENT_PLACEMENTS: Record<string, CaveatPlacement> = {
  'submitted-at-missing-on-redelivery': 'task-duration',
  'residual-is-not-only-tools': 'residual',
  'rejections-missing-from-executions': 'rejection-count',
  'point-in-time-only': 'not-on-this-page',
  'embedding-covers-search-only': 'not-on-this-page',
  'delegation-sides-are-not-mirrors': 'delegation-sides',
};

const PLACEMENTS: Record<CaveatSurface, Record<string, CaveatPlacement>> = {
  system: SYSTEM_PLACEMENTS,
  agent: AGENT_PLACEMENTS,
};

export function caveatLabel(code: string, surface: CaveatSurface = 'system'): CaveatLabel {
  const text = CAVEAT_TEXTS[code];
  if (text === undefined) {
    return { code, placement: 'unknown', text: code };
  }
  return { code, placement: PLACEMENTS[surface][code], text };
}

/**
 * Os códigos que devem aparecer numa posição de uma superfície.
 *
 * O desconhecido NÃO entra aqui — ele tem tratamento próprio, visível, e sai por
 * `unknownCaveats`.
 */
export function caveatsFor(
  codes: string[],
  placement: CaveatPlacement,
  surface: CaveatSurface = 'system',
): CaveatLabel[] {
  return codes.map((c) => caveatLabel(c, surface)).filter((c) => c.placement === placement);
}

/** Os códigos que a tela não conhece, para o aviso visível. */
export function unknownCaveats(
  codes: string[],
  surface: CaveatSurface = 'system',
): CaveatLabel[] {
  return codes.map((c) => caveatLabel(c, surface)).filter((c) => c.placement === 'unknown');
}

/** Exportado para o teste afirmar que o mapa é fechado nos seis. */
export const KNOWN_CAVEAT_CODES = Object.keys(CAVEAT_TEXTS);
