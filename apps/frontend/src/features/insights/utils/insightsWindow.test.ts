import { describe, expect, it } from 'vitest';
import {
  DEFAULT_INSIGHTS_PERIOD,
  INSIGHTS_PERIODS,
  insightsWindow,
  periodDays,
} from './insightsWindow';

const DAY_MS = 24 * 60 * 60 * 1000;

describe('insightsWindow', () => {
  const now = new Date('2026-09-24T18:30:00.000Z');

  it.each([
    ['7d', 7],
    ['30d', 30],
    ['90d', 90],
  ] as const)('%s cobre %i × 24 h terminando agora', (period, days) => {
    const { from, to } = insightsWindow(period, now);

    expect(to).toBe(now.toISOString());
    expect(new Date(to).getTime() - new Date(from).getTime()).toBe(days * DAY_MS);
  });

  it('"agora" é parâmetro, e a janela é determinística', () => {
    // Sem `new Date()` lá dentro: dois chamados com o mesmo instante produzem a
    // mesma janela, e é isso que torna o hook testável sem relógio falso.
    expect(insightsWindow('30d', now)).toEqual(insightsWindow('30d', now));
  });

  it('avançar o relógio rola a janela inteira', () => {
    // A janela é ROLANTE EM INSTANTES, não de calendário: uma hora depois, os
    // dois limites andaram uma hora — nenhum deles ancora em meia-noite.
    const umaHoraDepois = new Date(now.getTime() + 60 * 60 * 1000);
    const antes = insightsWindow('7d', now);
    const depois = insightsWindow('7d', umaHoraDepois);

    expect(new Date(depois.from).getTime() - new Date(antes.from).getTime()).toBe(
      60 * 60 * 1000,
    );
    expect(new Date(depois.to).getTime() - new Date(antes.to).getTime()).toBe(60 * 60 * 1000);
  });

  it('a saída é sempre UTC com Z', () => {
    const { from, to } = insightsWindow('90d', now);

    expect(from).toMatch(/Z$/);
    expect(to).toMatch(/Z$/);
  });

  it('30d é o padrão, e os três períodos são os do protótipo', () => {
    expect(DEFAULT_INSIGHTS_PERIOD).toBe('30d');
    expect(INSIGHTS_PERIODS).toEqual(['7d', '30d', '90d']);
    expect(INSIGHTS_PERIODS.map(periodDays)).toEqual([7, 30, 90]);
  });
});
