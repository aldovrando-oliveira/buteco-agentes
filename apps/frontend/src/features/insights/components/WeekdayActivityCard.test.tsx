import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { WeekdayActivityCard } from './WeekdayActivityCard';
import { temporalFixture, insightsWindowFixture } from '../test/systemInsightsFixture';
import { measuredDays } from '../utils/measuredDays';
import type { DailyInsightPoint, TemporalInsights } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';

const janela = insightsWindowFixture();

function renderCard(
  temporal: TemporalInsights,
  serie: DailyInsightPoint[],
  queryState: QueryState = 'ok',
) {
  render(
    <MantineProvider theme={theme}>
      <WeekdayActivityCard
        temporal={temporal}
        measured={measuredDays(janela, serie)}
        queryState={queryState}
      />
    </MantineProvider>,
  );
}

/** 22 = terça (2), 23 = quarta (3), 24 = quinta (4). */
const tresDias: DailyInsightPoint[] = [
  { day: '2026-09-22', taskCount: 26, tokenCount: 900_000 },
  { day: '2026-09-23', taskCount: 0, tokenCount: null },
  { day: '2026-09-24', taskCount: 31, tokenCount: 1_100_000 },
];

const valor = (weekday: number) => screen.getByTestId(`dia-da-semana-${weekday}-valor`);

describe('WeekdayActivityCard', () => {
  it('desenha as sete linhas, sempre', () => {
    renderCard(temporalFixture(), tresDias);

    for (const weekday of [0, 1, 2, 3, 4, 5, 6]) {
      expect(screen.getByTestId(`dia-da-semana-${weekday}`)).toBeInTheDocument();
    }
  });

  it('dia da semana com atividade aparece com a contagem', () => {
    renderCard(
      temporalFixture({ byWeekday: [{ weekday: 2, taskCount: 26 }] }),
      tresDias,
    );

    expect(valor(2)).toHaveTextContent('26');
    expect(valor(2)).toHaveAttribute('data-metric-state', 'value');
  });

  it('dia da semana COBERTO e ausente de byWeekday vira ZERO MEDIDO', () => {
    // Quarta (3) ocorre entre os dias da série e não aparece em `byWeekday`:
    // o dia foi medido e não teve task, então é zero — não vazio.
    renderCard(temporalFixture({ byWeekday: [{ weekday: 2, taskCount: 26 }] }), tresDias);

    expect(valor(3)).toHaveAttribute('data-metric-state', 'zero');
    expect(valor(3)).toHaveTextContent('0');
  });

  it('NEGATIVO: dia da semana NÃO coberto sai vazio, SEM 0', () => {
    // O guarda que importa. A série cobre só ter/qua/qui; os outros quatro
    // nunca foram medidos, e não há zero para dar. Um `?? 0` aqui afirmaria
    // que ninguém trabalhou na segunda — quando a medição nem alcançou uma
    // segunda-feira.
    renderCard(temporalFixture({ byWeekday: [{ weekday: 2, taskCount: 26 }] }), tresDias);

    for (const weekday of [0, 1, 5, 6]) {
      expect(valor(weekday)).toHaveAttribute('data-metric-state', 'empty');
      expect(valor(weekday).textContent).not.toContain('0');
      expect(screen.getByTestId(`dia-da-semana-${weekday}`)).toHaveAttribute(
        'data-covered',
        'false',
      );
    }
  });

  it('a cobertura sai da SÉRIE, não de byWeekday', () => {
    // `byWeekday` traz sexta com 0; a série não cobre sexta. A série vence, e a
    // linha sai vazia — porque `byWeekday` sozinho não distingue "medido e
    // zerado" de "nunca medido", que é o mesmo defeito que a #65 corrigiu na
    // série diária e que a spec da rota ainda não cobre para M6.
    renderCard(
      temporalFixture({
        byWeekday: [
          { weekday: 2, taskCount: 26 },
          { weekday: 5, taskCount: 0 },
        ],
      }),
      tresDias,
    );

    expect(valor(5)).toHaveAttribute('data-metric-state', 'empty');
    expect(valor(5).textContent).not.toContain('0');
  });

  it('marca o pico que a resposta declara', () => {
    renderCard(
      temporalFixture({ byWeekday: [{ weekday: 2, taskCount: 26 }], peakWeekday: 2 }),
      tresDias,
    );

    expect(screen.getByTestId('dia-da-semana-2')).toHaveAttribute('data-peak', 'true');
  });

  it('NEGATIVO: sem pico declarado, NENHUM dia é marcado', () => {
    // A rota devolve `peakWeekday: null` quando não há ocorrência. Eleger um
    // aqui — o primeiro, o maior de uma lista zerada — inventaria "domingo" como
    // pico de um período em que nada aconteceu.
    renderCard(
      temporalFixture({ byWeekday: [{ weekday: 2, taskCount: 26 }], peakWeekday: null }),
      tresDias,
    );

    for (const weekday of [0, 1, 2, 3, 4, 5, 6]) {
      expect(screen.getByTestId(`dia-da-semana-${weekday}`)).not.toHaveAttribute('data-peak');
    }
  });

  it('consulta sem resposta põe travessão em todas as sete, e nenhum 0', () => {
    renderCard(
      temporalFixture({ byWeekday: [{ weekday: 2, taskCount: 26 }] }),
      tresDias,
      'failed',
    );

    for (const weekday of [0, 1, 2, 3, 4, 5, 6]) {
      expect(valor(weekday)).toHaveAttribute('data-metric-state', 'unknown');
      expect(valor(weekday)).toHaveTextContent('—');
    }
    expect(screen.getByTestId('card-dias-da-semana').textContent).not.toContain('26');
  });

  it('declara em texto o que a célula vazia significa', () => {
    renderCard(temporalFixture(), tresDias);

    expect(screen.getByTestId('card-dias-da-semana')).toHaveTextContent(
      /não ocorreu na faixa medida/i,
    );
  });
});
