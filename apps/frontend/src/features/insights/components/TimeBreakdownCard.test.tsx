import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { TimeBreakdownCard } from './TimeBreakdownCard';
import { agentPerformanceFixture } from '../test/agentInsightsFixture';
import type { AgentPerformanceInsights } from '../types/agentInsights';
import type { QueryState } from '../utils/metricState';

function renderCard(
  performance: AgentPerformanceInsights,
  queryState: QueryState = 'ok',
) {
  const { container } = render(
    <MantineProvider theme={theme}>
      <TimeBreakdownCard
        performance={performance}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
  const faixas = () => [...container.querySelectorAll('[data-testid$="-faixa"]')];
  return { container, faixas };
}

const comTresParcelas = agentPerformanceFixture({
  taskDuration: { averageMs: 4400, p95Ms: 8400, sampleCount: 208 },
  queueTime: { averageMs: 300, p95Ms: 500, sampleCount: 208 },
  providerCallDuration: { averageMs: 3200, p95Ms: 5100, sampleCount: 238 },
  nonProviderResidual: { averageMs: 900, p95Ms: 1400, sampleCount: 205 },
});

describe('TimeBreakdownCard — as três parcelas e a legenda', () => {
  it('mostra as três, cada uma com o seu número', () => {
    renderCard(comTresParcelas);

    expect(screen.getByTestId('tempo-fila-valor')).toHaveTextContent('0,3 s');
    expect(screen.getByTestId('tempo-provedor-valor')).toHaveTextContent('3,2 s');
    expect(screen.getByTestId('tempo-residuo-valor')).toHaveTextContent('0,9 s');
  });

  it('a terceira parcela se chama FERRAMENTAS, como o artboard', () => {
    // A D4 a tinha renomeado para "Fora do provedor". O dono devolveu o nome
    // do desenho na conferência manual de 26/09 — e é por isso que o caveat
    // que avisa "não é só ferramentas" fica ainda mais necessário.
    renderCard(comTresParcelas);

    expect(screen.getByTestId('tempo-residuo')).toHaveTextContent('Ferramentas');
    expect(screen.getByTestId('card-onde-o-tempo-foi')).not.toHaveTextContent(
      'Fora do provedor',
    );
  });
});

describe('TimeBreakdownCard — a régua empilhada', () => {
  it('desenha as TRÊS faixas, proporcionais ao total', () => {
    const { faixas } = renderCard(comTresParcelas);

    expect(faixas()).toHaveLength(3);

    // A asserção é sobre a PROPORÇÃO, não sobre a string: comparar o texto do
    // estilo compara o arredondamento do ponto flutuante, e a primeira versão
    // deste caso reprovou por 0,0000000000000007%.
    const larguras = faixas().map((f) =>
      Number.parseFloat((f as HTMLElement).style.width),
    );

    // 0,3 + 3,2 + 0,9 = 4,4
    expect(larguras[0]).toBeCloseTo((300 / 4400) * 100, 6);
    expect(larguras[1]).toBeCloseTo((3200 / 4400) * 100, 6);
    expect(larguras[2]).toBeCloseTo((900 / 4400) * 100, 6);
    // E elas cobrem a barra inteira: nenhuma sobra, nenhuma falta.
    expect(larguras.reduce((a, b) => a + b, 0)).toBeCloseTo(100, 6);
  });

  it('o cabeçalho traz o total das três, e NÃO o chama de mediana nem de média', () => {
    // O número é a soma de três médias sobre populações diferentes. Chamá-lo
    // de "mediana por task" afirmaria que metade das tasks foi mais rápida;
    // de "média por task", uma média que a soma não produz. O rótulo diz o que
    // o número é.
    renderCard(comTresParcelas);

    expect(screen.getByTestId('tempo-total-valor')).toHaveTextContent('4,4 s');
    expect(screen.getByTestId('tempo-total')).toHaveTextContent('total das três');
    expect(screen.getByTestId('card-onde-o-tempo-foi')).not.toHaveTextContent(/mediana/i);
  });

  it('parcela DESCONHECIDA não é desenhada como faixa de largura zero', () => {
    // O que continua proibido pela spec: compor área a partir de conhecimento
    // parcial. Uma faixa de zero no lugar do desconhecido afirmaria que aquela
    // etapa não levou tempo nenhum.
    const { faixas } = renderCard(
      agentPerformanceFixture({
        queueTime: { averageMs: 300, p95Ms: 500, sampleCount: 5 },
        providerCallDuration: { averageMs: null, p95Ms: null, sampleCount: 0 },
        nonProviderResidual: { averageMs: 900, p95Ms: 1400, sampleCount: 5 },
      }),
    );

    expect(faixas()).toHaveLength(0);
    // E o total também não existe: um "total" que ignora a parcela nula
    // afirmaria que ela não pesou nada.
    expect(screen.getByTestId('tempo-total-valor')).toHaveAttribute(
      'data-metric-state',
      'empty',
    );
    // As parcelas conhecidas continuam na legenda.
    expect(screen.getByTestId('tempo-fila-valor')).toHaveTextContent('0,3 s');
    expect(screen.getByTestId('tempo-provedor-valor')).toHaveAttribute(
      'data-metric-state',
      'empty',
    );
  });

  it('a consulta em curso NÃO desenha faixa nem produz 0', () => {
    const { faixas } = renderCard(comTresParcelas, 'loading');

    expect(faixas()).toHaveLength(0);
    for (const id of ['tempo-fila-valor', 'tempo-provedor-valor', 'tempo-residuo-valor']) {
      const el = screen.getByTestId(id);
      expect(el).toHaveAttribute('data-metric-state', 'unknown');
      expect(el).not.toHaveTextContent('0');
    }
    expect(screen.getByTestId('tempo-total-valor')).toHaveAttribute(
      'data-metric-state',
      'unknown',
    );
  });

  it('total zero medido não desenha faixa — não há proporção sobre zero', () => {
    const { faixas } = renderCard(
      agentPerformanceFixture({
        queueTime: { averageMs: 0, p95Ms: 0, sampleCount: 3 },
        providerCallDuration: { averageMs: 0, p95Ms: 0, sampleCount: 3 },
        nonProviderResidual: { averageMs: 0, p95Ms: 0, sampleCount: 3 },
      }),
    );

    expect(faixas()).toHaveLength(0);
    expect(screen.getByTestId('tempo-total-valor')).toHaveAttribute(
      'data-metric-state',
      'zero',
    );
  });
});

describe('TimeBreakdownCard — uma explicação só, e é a do artboard', () => {
  it('a nota é a do desenho', () => {
    renderCard(comTresParcelas);

    expect(screen.getByTestId('tempo-nota')).toHaveTextContent(
      'Ferramentas é o que sobra depois de descontar as chamadas ao provedor da duração total — não é medido diretamente.',
    );
  });

  it('há UM parágrafo no rodapé, não dois', () => {
    const { container } = renderCard(comTresParcelas);

    const paragrafos = [...container.querySelectorAll('p')].map((e) => e.textContent ?? '');
    // A nota das populações diferentes saiu: o dono aceitou a soma sabendo o
    // que ela é, e o registro dela vive no `02` e na issue #81.
    expect(paragrafos.join(' ')).not.toContain('As três não se somam');
    expect(paragrafos.join(' ')).not.toContain('medições');
  });

  it('o caveat do resíduo virou ÍCONE no cabeçalho, com o texto inteiro', () => {
    // Com o nome "Ferramentas" de volta, ele é o único aviso na tela de que o
    // resíduo inclui espera de lock, MCP e busca vetorial. Descartá-lo seria
    // deixar o rótulo afirmar mais do que o número sabe.
    renderCard(comTresParcelas);

    const caveat = screen.getByTestId('tempo-residuo-caveat');
    expect(caveat).toHaveAttribute('data-caveat-code', 'residual-is-not-only-tools');
    expect(caveat).toHaveAttribute(
      'title',
      expect.stringContaining('espera de lock, chamadas MCP e busca vetorial'),
    );
  });
});
