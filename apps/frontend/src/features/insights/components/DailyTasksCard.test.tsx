import { describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { DailyTasksCard } from './DailyTasksCard';
import { insightsWindowFixture } from '../test/systemInsightsFixture';
import { measuredDays } from '../utils/measuredDays';
import type { DailyInsightPoint } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';

const janela = insightsWindowFixture();

function renderSerie(serie: DailyInsightPoint[], queryState: QueryState = 'ok') {
  render(
    <MantineProvider theme={theme}>
      <DailyTasksCard measured={measuredDays(janela, serie)} queryState={queryState} />
    </MantineProvider>,
  );
}

const serie: DailyInsightPoint[] = [
  { day: '2026-09-22', taskCount: 26, tokenCount: null },
  { day: '2026-09-23', taskCount: 0, tokenCount: null },
  { day: '2026-09-24', taskCount: 33, tokenCount: null },
];

describe('DailyTasksCard', () => {
  it('a série medida vira pontos', () => {
    renderSerie(serie);

    expect(screen.getByTestId('ponto-2026-09-22')).toBeInTheDocument();
    expect(screen.getByTestId('ponto-2026-09-23')).toBeInTheDocument();
    expect(screen.getByTestId('ponto-2026-09-24')).toBeInTheDocument();
  });

  it('NEGATIVO: o prefixo não medido NÃO vira ponto', () => {
    renderSerie(serie);

    // Os sete dias anteriores ao regime não têm ponto. Um ponto em zero ali
    // afirmaria "contei e deu zero" para um dia que ninguém mediu.
    for (const dia of ['2026-09-15', '2026-09-18', '2026-09-21']) {
      expect(screen.queryByTestId(`ponto-${dia}`)).toBeNull();
    }
    expect(screen.getAllByTestId(/^ponto-/)).toHaveLength(3);
  });

  it('o prefixo não medido vira FAIXA hachurada, e as listras são CSS', () => {
    // A primeira versão usava um `<pattern>` de SVG com
    // `patternTransform="rotate(135)"`, dentro de um SVG com
    // `preserveAspectRatio="none"` — a escala não uniforme deformava o padrão e
    // as listras saíam invisíveis na tela. O protótipo usa
    // `repeating-linear-gradient`, e agora a faixa também.
    renderSerie(serie);

    const faixa = screen.getByTestId('faixa-nao-medida-0');
    const estilo = faixa.getAttribute('style') ?? '';
    expect(estilo).toContain('repeating-linear-gradient(135deg');
    expect(estilo).toContain('var(--buteco-surface-subtle)');
    expect(estilo).toContain('dashed');
  });

  it('a faixa cobre exatamente os dias não medidos', () => {
    // Sete dos dez dias da janela são não medidos, e são os sete primeiros:
    // a faixa começa em 0% e ocupa 70%.
    renderSerie(serie);

    const estilo = screen.getByTestId('faixa-nao-medida-0').getAttribute('style') ?? '';
    expect(estilo).toContain('left: 0%');
    expect(estilo).toContain('width: 70%');
  });

  it('NEGATIVO: a hachura não sai de var() em atributo de SVG', () => {
    // O modo de falha exato da primeira versão: `var()` em atributo de
    // apresentação, dentro de um viewBox esticado. O desenho fica no SVG; a
    // textura, fora dele.
    renderSerie(serie);

    expect(document.querySelector('pattern')).toBeNull();
    expect(screen.getByTestId('tasks-por-dia-svg').querySelector('defs')).toBeNull();
  });

  it('a linha QUEBRA no trecho não medido em vez de atravessá-lo', () => {
    // Buraco no meio: 22 medido, 23 ausente, 24 medido. São DOIS segmentos, e
    // não um só atravessando o vão — uma linha contínua ali afirmaria uma
    // trajetória entre dois pontos que não se ligam.
    renderSerie([
      { day: '2026-09-22', taskCount: 26, tokenCount: null },
      { day: '2026-09-24', taskCount: 33, tokenCount: null },
    ]);

    const segmentos = screen.getAllByTestId(/^segmento-/);
    expect(segmentos).toHaveLength(2);
    // E cada um tem um ponto só, então nenhum deles atravessa o dia 23.
    for (const segmento of segmentos) {
      expect(segmento.getAttribute('points')?.trim().split(' ')).toHaveLength(1);
    }
  });

  it('a linha é contínua onde os dias medidos são contíguos', () => {
    renderSerie(serie);

    const segmentos = screen.getAllByTestId(/^segmento-/);
    expect(segmentos).toHaveLength(1);
    expect(segmentos[0].getAttribute('points')?.trim().split(' ')).toHaveLength(3);
  });

  it('um dia MEDIDO com zero entra como ponto em zero, e não some', () => {
    // A outra metade da distinção: medido e vazio é um fato, e o gráfico o
    // mostra no eixo. Só o NÃO medido some.
    renderSerie(serie);

    const ponto = screen.getByTestId('ponto-2026-09-23');
    expect(ponto).toHaveAttribute('data-day-state', 'measured-zero');
    // No eixo: `cy` é o fundo do viewBox menos o padding.
    expect(Number(ponto.getAttribute('cy'))).toBe(116);
  });

  it('o rótulo acessível diz quantos dias foram medidos', () => {
    renderSerie(serie);

    expect(screen.getByTestId('tasks-por-dia-svg')).toHaveAccessibleName(
      /3 dias medidos de 10 pedidos/,
    );
  });

  it('série toda não medida não quebra e não desenha ponto nenhum', () => {
    renderSerie([]);

    expect(screen.queryAllByTestId(/^ponto-/)).toHaveLength(0);
    expect(screen.getByTestId('tasks-por-dia-svg')).toHaveAccessibleName(/sem medição/);
  });

  it('consulta sem resposta mostra travessão, e nenhum gráfico', () => {
    renderSerie(serie, 'failed');

    expect(screen.getByTestId('tasks-por-dia-travessao')).toHaveTextContent('—');
    expect(screen.queryByTestId('tasks-por-dia-svg')).toBeNull();
    expect(screen.getByTestId('card-tasks-por-dia').textContent).not.toContain('26');
  });

  // ------------------------------- O QUE O ARTBOARD DESENHA E FALTAVA

  it('o cabeçalho traz máximo e mínimo da faixa MEDIDA, à direita', () => {
    // O `Main.dc.html` escreve "máximo 33 · mínimo 0" no canto do cabeçalho. A
    // primeira versão tinha os dois só no `aria-label` — presentes para leitor
    // de tela, invisíveis para quem enxerga.
    renderSerie(serie);

    expect(screen.getByTestId('tasks-por-dia-extremos')).toHaveTextContent(
      'máximo 33 · mínimo 0',
    );
  });

  it('o mínimo sai dos dias MEDIDOS, e não da janela', () => {
    // O dia 23 é medido e vazio: mínimo 0 é um zero CONTADO. Os sete dias não
    // medidos não entram — eles não têm contagem para ser mínimo de nada.
    renderSerie([
      { day: '2026-09-22', taskCount: 26, tokenCount: null },
      { day: '2026-09-24', taskCount: 33, tokenCount: null },
    ]);

    expect(screen.getByTestId('tasks-por-dia-extremos')).toHaveTextContent(
      'máximo 33 · mínimo 26',
    );
  });

  it('NEGATIVO: sem dia medido, o cabeçalho não afirma máximo nem mínimo', () => {
    // "máximo 0 · mínimo 0" afirmaria medição onde não houve nenhuma.
    renderSerie([]);

    expect(screen.queryByTestId('tasks-por-dia-extremos')).toBeNull();
    expect(screen.getByTestId('card-tasks-por-dia').textContent).not.toMatch(/máximo/);
  });

  it('o eixo traz três datas — primeira, meio e última da janela', () => {
    renderSerie(serie);

    const eixo = screen.getByTestId('tasks-por-dia-eixo');
    expect(eixo).toHaveTextContent('15/09');
    expect(eixo).toHaveTextContent('19/09');
    expect(eixo).toHaveTextContent('24/09');
  });

  it('as datas do eixo são as do fuso DA RESPOSTA', () => {
    // Mesma regra do resto da tela: o dia vem de `measuredDays`, que converte
    // pelo fuso que a resposta declara — nunca pelo do navegador.
    renderSerie(serie);

    // A janela vai de 15/09 03:00Z a 25/09 02:59Z, que em America/Sao_Paulo é
    // 15/09 a 24/09. Em UTC a última seria 25/09.
    expect(screen.getByTestId('tasks-por-dia-eixo').textContent).not.toContain('25/09');
  });

  it('janela de um dia não repete a mesma data três vezes', () => {
    renderSerie([], 'ok');
    // A janela da fixture tem 10 dias; este caso monta uma de um dia só.
    cleanup();
    render(
      <MantineProvider theme={theme}>
        <DailyTasksCard
          measured={measuredDays(
            { from: '2026-09-22T03:00:00+00:00', to: '2026-09-22T23:00:00+00:00', timeZone: 'America/Sao_Paulo' },
            [{ day: '2026-09-22', taskCount: 5, tokenCount: null }],
          )}
          queryState="ok"
        />
      </MantineProvider>,
    );

    expect(screen.getAllByTestId(/^eixo-/)).toHaveLength(1);
  });

  // ------------------------------------- A LEGENDA É CONDICIONAL (por estado)

  it('a legenda da hachura aparece quando HÁ trecho não medido', () => {
    renderSerie(serie);

    // Literal do quadro 1 do `Estados.dc.html`.
    expect(screen.getByTestId('tasks-por-dia-legenda-hachura')).toHaveTextContent(
      'A faixa hachurada é o trecho sem medição. Preencher com zero faria a série inventar um período de inatividade que nunca existiu.',
    );
  });

  it('NEGATIVO: sem trecho não medido, a legenda NÃO aparece', () => {
    // É o caso do `Main.dc.html`: a série cobre a janela inteira, não há
    // hachura, e um texto explicando uma faixa que não aparece não tem o que
    // explicar. A primeira versão a renderizava sempre.
    cleanup();
    render(
      <MantineProvider theme={theme}>
        <DailyTasksCard
          measured={measuredDays(
            { from: '2026-09-22T03:00:00+00:00', to: '2026-09-23T23:00:00+00:00', timeZone: 'America/Sao_Paulo' },
            [
              { day: '2026-09-22', taskCount: 5, tokenCount: null },
              { day: '2026-09-23', taskCount: 7, tokenCount: null },
            ],
          )}
          queryState="ok"
        />
      </MantineProvider>,
    );

    expect(screen.queryByTestId('tasks-por-dia-legenda-hachura')).toBeNull();
    expect(screen.queryAllByTestId(/^faixa-nao-medida-/)).toHaveLength(0);
  });

  it('NEGATIVO: a legenda não parafraseia o artboard', () => {
    // "A linha quebra ali em vez de cair a zero" era argumento meu, não do
    // autor. O protótipo vence, e o texto dele é literal.
    renderSerie(serie);

    expect(
      screen.getByTestId('tasks-por-dia-legenda-hachura').textContent,
    ).not.toMatch(/a linha quebra/i);
  });
});
