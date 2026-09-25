import { describe, expect, it } from 'vitest';
import { HEAT_STEPS, heatStep, heatVariable } from './heatScale';

describe('heatStep', () => {
  it('distribui uma série normal nas cinco faixas', () => {
    const max = 33;
    const passos = [1, 8, 15, 22, 33].map((n) => heatStep(n, max));

    expect(passos).toEqual([1, 2, 3, 4, 5]);
  });

  it('o mínimo medido cai em 1 e o máximo em 5', () => {
    expect(heatStep(1, 33)).toBe(1);
    expect(heatStep(33, 33)).toBe(5);
  });

  it('série de valor único não quebra a escala', () => {
    // Todos os dias com a mesma contagem: max === count, e todos caem num passo
    // VÁLIDO. Sem a guarda, `(count - 1) / (max - 1)` seria 0/0 = NaN.
    const passos = [7, 7, 7].map((n) => heatStep(n, 7));

    expect(passos).toEqual([5, 5, 5]);
    for (const passo of passos) {
      expect(HEAT_STEPS).toContain(passo);
      expect(Number.isNaN(passo)).toBe(false);
    }
  });

  it('máximo 1 não divide por zero', () => {
    const passo = heatStep(1, 1);

    expect(passo).toBe(5);
    expect(Number.isFinite(passo)).toBe(true);
  });

  it('máximo nulo não produz NaN', () => {
    // `maxMeasuredCount` é nulo quando não há nenhum dia medido — e aí não há
    // célula medida para pintar, mas a função não pode explodir se chamada.
    expect(Number.isFinite(heatStep(3, null))).toBe(true);
  });

  it('o zero medido NUNCA recebe passo de intensidade', () => {
    // O guarda negativo: o passo 0 é reservado ao zero medido, e nenhum dos
    // cinco passos de intensidade pode alcançá-lo. Se o zero caísse em 1, a
    // célula de "contei e deu zero" ficaria visualmente igual à de "um dia
    // fraquinho", e a distinção que a página inteira defende sumiria no mapa.
    expect(heatStep(0, 33)).toBe(0);
    expect(heatStep(0, 1)).toBe(0);
    expect(heatStep(0, null)).toBe(0);

    for (const max of [1, 7, 33, null]) {
      expect(heatStep(0, max)).not.toBe(1);
    }
  });

  it('todo passo devolvido está dentro da escala declarada', () => {
    for (const count of [0, 1, 2, 5, 13, 29, 33, 100]) {
      expect(HEAT_STEPS).toContain(heatStep(count, 33));
    }
  });

  it('a variável do tema é o que o componente lê', () => {
    // Nenhum tom da paleta aparece aqui: o componente recebe
    // `var(--buteco-heat-N)` e a rampa inverte no tema, não no código.
    expect(heatVariable(0)).toBe('var(--buteco-heat-0)');
    expect(heatVariable(5)).toBe('var(--buteco-heat-5)');
    expect(heatVariable(3)).not.toMatch(/#/);
  });
});
