// OS CÓDIGOS DE PARCIALIDADE — MAPA FECHADO, COM A POSIÇÃO DE CADA UM (D12).
//
// A rota entrega os cinco códigos em DOIS blocos: `performance` (2) e `errors`
// (3). Conferido no corpo real em 23/09 e reconferido em 24/09 — os outros seis
// blocos não têm o campo `caveats`.
//
// MAS O BLOCO QUE CARREGA O CÓDIGO NÃO É O ÚNICO QUE ELE LIMITA, e é por isso
// que a posição é declarada aqui em vez de derivada do bloco. Renderizar os
// cinco numa lista ao pé do bloco em que chegaram seria texto de limitação longe
// do número limitado — que é exatamente a forma de ninguém ler.
//
// UM CÓDIGO SEM NÚMERO NESTA PÁGINA NÃO É RENDERIZADO. `residual-is-not-only-tools`
// qualifica o resíduo (M25), que o `Main.dc.html` não desenha; um texto de
// limitação sem o número que ele limita não tem o que qualificar. Isso NÃO é
// descartar em silêncio: o campo está no `02` como item aberto, e o teste afirma
// a classificação por escrito.
//
// CÓDIGO DESCONHECIDO APARECE CRU, como aviso visível. É a convenção da casa
// para enum desconhecido: indicador neutro, nunca silêncio, nunca reaproveitar o
// indicador de outra coisa.

/** Onde na página o texto do código é renderizado. */
export type CaveatPlacement =
  | 'task-duration'
  | 'rejection-count'
  | 'rejection-reason'
  | 'non-terminal'
  /** O número que ele limita não está nesta página — não renderiza (D12). */
  | 'not-on-this-page'
  /** A tela não conhece o código — renderiza cru, como aviso. */
  | 'unknown';

export interface CaveatLabel {
  code: string;
  placement: CaveatPlacement;
  /** O texto de operador, ou o próprio código quando desconhecido. */
  text: string;
}

interface KnownCaveat {
  placement: CaveatPlacement;
  text: string;
}

const CAVEATS: Record<string, KnownCaveat> = {
  'submitted-at-missing-on-redelivery': {
    placement: 'task-duration',
    text: 'Execuções reentregues ficam fora da média: nelas o instante de submissão não é registrado.',
  },
  'residual-is-not-only-tools': {
    // O resíduo não tem elemento no protótipo, então não há número para
    // qualificar. Classificado, não esquecido.
    placement: 'not-on-this-page',
    text: 'O tempo residual inclui espera de lock, chamadas MCP e busca vetorial, não só ferramentas.',
  },
  'rejections-missing-from-executions': {
    placement: 'rejection-count',
    text: 'Recusas não geram linha de execução, então não entram no percentual de falha.',
  },
  'rejection-reason-not-collected': {
    // SEM POSIÇÃO NESTA PÁGINA, pela regra que a própria spec dá: "código cujo
    // número não é apresentado nesta página SHALL NOT ser renderizado — um
    // texto de limitação sem o número que ele limita não tem o que qualificar".
    //
    // Ele qualifica o MOTIVO da recusa, e o motivo saiu da tela quando as
    // lacunas em moldura foram removidas (decisão do dono, décima rodada): o
    // quadro tracejado não existe no protótipo e está fora do padrão do
    // painel. Sem o elemento que nomeava o motivo, não há número para o caveat
    // qualificar — e mantê-lo como legenda solta seria acrescentar outro texto
    // que o artboard não tem.
    //
    // A CONTAGEM de recusas continua na tela, no card de Falhas, com o seu
    // próprio caveat (`rejections-missing-from-executions`). O que saiu foi o
    // motivo, que a rota não serve e que vive na issue #51.
    placement: 'not-on-this-page',
    text: 'O motivo da recusa não é coletado hoje.',
  },
  'point-in-time-only': {
    placement: 'non-terminal',
    text: 'Contagem do instante da consulta: não diz há quanto tempo as tasks estão neste estado.',
  },
};

export function caveatLabel(code: string): CaveatLabel {
  const known = CAVEATS[code];
  if (known === undefined) {
    return { code, placement: 'unknown', text: code };
  }
  return { code, placement: known.placement, text: known.text };
}

/**
 * Os códigos que devem aparecer numa posição da página.
 *
 * O desconhecido NÃO entra aqui — ele tem tratamento próprio, visível, e sai por
 * `unknownCaveats`.
 */
export function caveatsFor(codes: string[], placement: CaveatPlacement): CaveatLabel[] {
  return codes.map(caveatLabel).filter((c) => c.placement === placement);
}

/** Os códigos que a tela não conhece, para o aviso visível. */
export function unknownCaveats(codes: string[]): CaveatLabel[] {
  return codes.map(caveatLabel).filter((c) => c.placement === 'unknown');
}

/** Exportado para o teste afirmar que o mapa é fechado nos cinco. */
export const KNOWN_CAVEAT_CODES = Object.keys(CAVEATS);
