import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { PeriodHeatmapCard } from './PeriodHeatmapCard';
import { insightsWindowFixture } from '../test/systemInsightsFixture';
import { measuredDays } from '../utils/measuredDays';
import type { DailyInsightPoint } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';

const janela = insightsWindowFixture();

function renderMapa(serie: DailyInsightPoint[], queryState: QueryState = 'ok') {
  render(
    <MantineProvider theme={theme}>
      <PeriodHeatmapCard
        measured={measuredDays(janela, serie)}
        timeZone={janela.timeZone}
        queryState={queryState}
      />
    </MantineProvider>,
  );
}

const serie: DailyInsightPoint[] = [
  { day: '2026-09-22', taskCount: 26, tokenCount: null },
  { day: '2026-09-23', taskCount: 0, tokenCount: null },
  { day: '2026-09-24', taskCount: 33, tokenCount: null },
];

describe('PeriodHeatmapCard — os quatro estados de célula', () => {
  it('desenha uma célula por dia da janela', () => {
    renderMapa(serie);

    // A janela tem 10 dias (15 a 24 de setembro).
    const celulas = screen.getAllByTestId(/^celula-/);
    expect(celulas).toHaveLength(10);
  });

  it('dia medido usa um passo da escala de intensidade', () => {
    renderMapa(serie);

    const celula = screen.getByTestId('celula-2026-09-24');
    expect(celula).toHaveAttribute('data-cell', 'measured');
    expect(celula).toHaveAttribute('data-heat-step', '5');
  });

  it('a intensidade é proporcional à contagem dentro da série medida', () => {
    renderMapa(serie);

    const menor = screen.getByTestId('celula-2026-09-22').getAttribute('data-heat-step');
    const maior = screen.getByTestId('celula-2026-09-24').getAttribute('data-heat-step');
    expect(Number(maior)).toBeGreaterThan(Number(menor));
  });

  it('zero medido recebe o PASSO PRÓPRIO, e não o passo 1', () => {
    renderMapa(serie);

    const celula = screen.getByTestId('celula-2026-09-23');
    expect(celula).toHaveAttribute('data-cell', 'zero');
    expect(celula).toHaveAttribute('data-heat-step', '0');
    expect(celula).not.toHaveAttribute('data-heat-step', '1');
  });

  it('NEGATIVO: dia não medido NÃO usa nenhum passo da escala', () => {
    // O guarda. Qualquer passo aqui — inclusive o do zero — diria que aquele
    // dia foi medido. A hachura é outra TEXTURA, não um sexto tom, e é isso
    // que a distingue do zero medido mesmo em monocromático.
    renderMapa(serie);

    for (const dia of [
      '2026-09-15',
      '2026-09-16',
      '2026-09-17',
      '2026-09-18',
      '2026-09-19',
      '2026-09-20',
      '2026-09-21',
    ]) {
      const celula = screen.getByTestId(`celula-${dia}`);
      expect(celula).toHaveAttribute('data-cell', 'unmeasured');
      expect(celula).not.toHaveAttribute('data-heat-step');
      expect(celula.getAttribute('style')).not.toContain('--buteco-heat-');
    }
  });

  it('o não medido é DISTINTO do zero medido e de todos os passos', () => {
    renderMapa(serie);

    const naoMedido = screen.getByTestId('celula-2026-09-15').getAttribute('style');
    const zero = screen.getByTestId('celula-2026-09-23').getAttribute('style');

    expect(naoMedido).not.toBe(zero);
    expect(naoMedido).toContain('repeating-linear-gradient');
    expect(zero).toContain('--buteco-heat-0');
  });

  it('NEGATIVO: nenhum dia não medido apresenta contagem', () => {
    renderMapa(serie);

    const celula = screen.getByTestId('celula-2026-09-15');
    expect(celula).toHaveAccessibleName('2026-09-15: não medido');
    expect(celula.getAttribute('aria-label')).not.toMatch(/\d+ tasks/);
  });

  it('posição fora da janela fica SEM célula', () => {
    renderMapa(serie);

    // 2026-09-15 é terça; com a grade começando na segunda, há exatamente uma
    // posição vazia antes dela — e ela não é uma célula de dia.
    const grade = screen.getByTestId('mapa-de-calor-grade');
    const semCelula = grade.querySelectorAll('[data-cell="none"]');
    expect(semCelula).toHaveLength(1);
    for (const vazia of semCelula) {
      expect(vazia).not.toHaveAttribute('data-heat-step');
      expect(vazia.getAttribute('title')).toBeNull();
    }
  });

  it('série de valor único não quebra a escala', () => {
    renderMapa([
      { day: '2026-09-22', taskCount: 7, tokenCount: null },
      { day: '2026-09-23', taskCount: 7, tokenCount: null },
    ]);

    for (const dia of ['2026-09-22', '2026-09-23']) {
      const passo = screen.getByTestId(`celula-${dia}`).getAttribute('data-heat-step');
      expect(['1', '2', '3', '4', '5']).toContain(passo);
    }
  });

  it('a legenda lê as variáveis do tema, e nenhum tom cravado', () => {
    renderMapa(serie);

    for (const step of [0, 1, 2, 3, 4, 5]) {
      const estilo = screen.getByTestId(`legenda-passo-${step}`).getAttribute('style') ?? '';
      expect(estilo).toContain(`var(--buteco-heat-${step})`);
      expect(estilo).not.toMatch(/#[0-9a-f]{6}/i);
    }
  });

  it('a nota de fuso diz o fuso DA RESPOSTA', () => {
    renderMapa(serie);

    expect(screen.getByTestId('card-mapa-de-calor')).toHaveTextContent(
      'horário local (America/Sao_Paulo)',
    );
  });

  it('a legenda declara o que a hachura significa', () => {
    renderMapa(serie);

    expect(screen.getByTestId('card-mapa-de-calor')).toHaveTextContent(
      /não medido.*não é dia sem uso/i,
    );
  });
});
