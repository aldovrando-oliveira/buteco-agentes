// A QUANTIZAÇÃO DO MAPA DE CALOR — DO CLIENTE, LINEAR, SOBRE A SÉRIE MEDIDA.
//
// O PROTÓTIPO CRAVA LIMIARES (8 / 16 / 24 / 30) E ELES NÃO ENTRAM. Só fazem
// sentido para os dados de exemplo dele: num sistema com 3 tasks por dia todas
// as células cairiam na primeira faixa, e num com 3 mil todas na última. A
// escala precisa nascer da série que está na tela.
//
// CINCO FAIXAS IGUAIS SOBRE `[1, máximo medido]`. O zero medido tem passo
// próprio (`0`), e o dia NÃO medido não usa a escala — é hachura, decidida pelo
// componente, e nunca um passo de intensidade.
//
// ALTERNATIVA RECUSADA: quantis da distribuição. Recusada por não ter pedido, e
// porque com poucos dias medidos o quantil inverte a leitura de intensidade sem
// aviso — dois dias iguais cairiam em faixas diferentes.
//
// Função pura, testada sem jsdom.

/** 0 é o passo do zero medido; 1 a 5 são as faixas de intensidade. */
export type HeatStep = 0 | 1 | 2 | 3 | 4 | 5;

export const HEAT_STEPS: readonly HeatStep[] = [0, 1, 2, 3, 4, 5] as const;

/** A variável do tema para um passo. O componente lê isto, nunca um tom. */
export function heatVariable(step: HeatStep): string {
  return `var(--buteco-heat-${step})`;
}

/**
 * O passo de um dia medido.
 *
 * `max` é o maior valor MEDIDO da série — nunca o maior da janela, que incluiria
 * os não medidos.
 */
export function heatStep(count: number, max: number | null): HeatStep {
  // Zero medido tem passo próprio: ele foi contado, e não é a mesma coisa que a
  // menor intensidade.
  if (count <= 0) {
    return 0;
  }

  // SÉRIE DE VALOR ÚNICO, E `max === 1`. Sem esta guarda, `(max - 1)` seria 0 e
  // a divisão devolveria `Infinity` ou `NaN` — todas as células medidas caem no
  // passo mais alto, que é o que "todos os dias iguais" quer dizer.
  if (max === null || max <= 1) {
    return 5;
  }

  // Cinco faixas iguais sobre [1, max]. `count === 1` cai em 1, `count === max`
  // cai em 5, e o `min` guarda o caso de um `count` acima do máximo declarado.
  const fraction = (count - 1) / (max - 1);
  return Math.min(5, Math.floor(fraction * 5) + 1) as HeatStep;
}
