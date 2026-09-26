import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { TaskDurationCard } from './TaskDurationCard';
import { agentPerformanceFixture } from '../test/agentInsightsFixture';
import type { AgentPerformanceInsights } from '../types/agentInsights';
import type { QueryState } from '../utils/metricState';
import { METRIC_SIZE } from '../utils/metricState';

function renderCard(
  performance: AgentPerformanceInsights,
  queryState: QueryState = 'ok',
) {
  const { container } = render(
    <MantineProvider theme={theme}>
      <TaskDurationCard
        performance={performance}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
  return container;
}

const medida = agentPerformanceFixture({
  taskDuration: { averageMs: 4400, p95Ms: 8400, sampleCount: 208 },
  providerCallsPerTask: 1.1,
});

describe('TaskDurationCard', () => {
  it('os três quadros do artboard, com os números', () => {
    renderCard(medida);

    expect(screen.getByTestId('duracao-media-valor')).toHaveTextContent('4,4 s');
    expect(screen.getByTestId('duracao-p95-valor')).toHaveTextContent('8,4 s');
    expect(screen.getByTestId('duracao-chamadas-por-task-valor')).toHaveTextContent('1,1');
  });

  it('a palavra é MÉDIA, e "mediana" não aparece em lugar nenhum (D3)', () => {
    // O artboard escreve "Mediana". A rota serve `avg(ms)` — é a L5, issue #66.
    // Chamar média de mediana afirma que metade das tasks foi mais rápida, o
    // que uma média com cauda longa não diz.
    renderCard(medida);

    expect(screen.getByTestId('duracao-media')).toHaveTextContent('Média');
    expect(screen.getByTestId('card-duracao-da-task')).not.toHaveTextContent(/mediana/i);
  });

  it('o card tem SÓ os três quadros — nenhum parágrafo no rodapé', () => {
    // O artboard desenha rótulo, os três quadros, e nada mais. As duas frases
    // que estavam aqui saíram na conferência manual de 26/09.
    const container = renderCard(medida);

    expect(screen.queryByTestId('duracao-nota-escopo')).not.toBeInTheDocument();
    const paragrafos = [...container.querySelectorAll('p')].map((e) => e.textContent ?? '');
    expect(paragrafos.join(' ')).not.toContain('Execuções reentregues');
    expect(paragrafos.join(' ')).not.toContain('fila incluída');
  });

  it('o caveat de reentrega virou ÍCONE no cabeçalho, com o texto inteiro', () => {
    // Ele NÃO foi descartado: o número que ele limita está neste card, e ele
    // não está duplicado em nenhum outro — a régua das rodadas anteriores.
    renderCard(medida);

    const caveat = screen.getByTestId('duracao-caveat');
    expect(caveat).toHaveAttribute('data-caveat-code', 'submitted-at-missing-on-redelivery');
    expect(caveat).toHaveAttribute(
      'title',
      expect.stringContaining('Execuções reentregues ficam fora da média'),
    );
  });

  it('o número usa o tamanho de dentro de card, medido no artboard', () => {
    // A escala do tema para em `xl: 16px`, feita para TEXTO. Número dentro de
    // card é 20px no artboard, e usar `xl` o entregaria a 80% do desenhado —
    // defeito que jsdom não pega, porque `size="xl"` é valor válido. O que se
    // afirma é a MEDIDA.
    //
    // O Mantine escreve o tamanho em `--text-fz`, em rem: 1.25rem = 20px.
    renderCard(medida);

    expect(METRIC_SIZE.card).toBe('20px');
    for (const id of ['duracao-media-valor', 'duracao-p95-valor']) {
      expect(screen.getByTestId(id).getAttribute('style') ?? '').toContain('1.25rem');
    }
  });

  it('fonte nula fica VAZIA — não 0 s', () => {
    renderCard(agentPerformanceFixture());

    for (const id of [
      'duracao-media-valor',
      'duracao-p95-valor',
      'duracao-chamadas-por-task-valor',
    ]) {
      const el = screen.getByTestId(id);
      expect(el).toHaveAttribute('data-metric-state', 'empty');
      expect(el).not.toHaveTextContent('0');
    }
  });

  it('a consulta em curso NÃO produz 0', () => {
    renderCard(medida, 'loading');

    const el = screen.getByTestId('duracao-media-valor');
    expect(el).toHaveAttribute('data-metric-state', 'unknown');
    expect(el).not.toHaveTextContent('0');
  });

  it('a consulta falhada traz a razão ao lado do travessão', () => {
    renderCard(medida, 'failed');

    expect(screen.getByTestId('duracao-p95-valor')).toHaveAttribute(
      'title',
      'A consulta não respondeu.',
    );
  });
});
