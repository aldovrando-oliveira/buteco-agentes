import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentKpiGrid } from './AgentKpiGrid';
import {
  agentErrorsFixture,
  agentInsightsFixture,
  agentTokensFixture,
  agentVolumeFixture,
} from '../test/agentInsightsFixture';
import type { AgentInsights } from '../types/agentInsights';
import type { QueryState } from '../utils/metricState';

function renderGrid(insights: AgentInsights, queryState: QueryState = 'ok') {
  render(
    <MantineProvider theme={theme}>
      <AgentKpiGrid
        insights={insights}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
}

describe('AgentKpiGrid — os quatro cards do artboard', () => {
  it('são QUATRO, e não os seis da página do sistema', () => {
    renderGrid(agentInsightsFixture());

    // O que a rota serve e o artboard não desenha fica fora (D10). Se alguém
    // acrescentar um card por conta, este caso aponta.
    expect(screen.getByTestId('kpi-agente-tasks')).toBeInTheDocument();
    expect(screen.getByTestId('kpi-agente-tokens')).toBeInTheDocument();
    expect(screen.getByTestId('kpi-agente-tokens-por-task')).toBeInTheDocument();
    expect(screen.getByTestId('kpi-agente-falha')).toBeInTheDocument();
    expect(screen.queryByTestId('kpi-agente-embedding')).not.toBeInTheDocument();
    expect(screen.queryByTestId('kpi-agente-chamadas')).not.toBeInTheDocument();
  });

  it('tasks executadas traz a origem discriminada nos dois lados', () => {
    renderGrid(
      agentInsightsFixture({
        volume: agentVolumeFixture({
          executedTaskCount: 97,
          externalOriginTaskCount: 0,
          delegationOriginTaskCount: 97,
        }),
      }),
    );

    expect(screen.getByTestId('kpi-agente-tasks-valor')).toHaveTextContent('97');
    // O zero por extenso, como o `Agente-Misto.dc.html` o escreve.
    expect(screen.getByTestId('kpi-agente-tasks-sub')).toHaveTextContent(
      'nenhuma de origem externa · 97 por delegação',
    );
  });

  it('o zero da origem por delegação também sai por extenso', () => {
    renderGrid(
      agentInsightsFixture({
        volume: agentVolumeFixture({
          executedTaskCount: 208,
          externalOriginTaskCount: 208,
          delegationOriginTaskCount: 0,
        }),
      }),
    );

    expect(screen.getByTestId('kpi-agente-tasks-sub')).toHaveTextContent(
      '208 de origem externa · nenhuma por delegação',
    );
  });

  it('tokens de conversa é a soma que PRESERVA o nulo', () => {
    renderGrid(
      agentInsightsFixture({
        tokens: agentTokensFixture({
          conversation: { inputTokens: 4_900_000, outputTokens: 1_300_000, cachedInputTokens: null },
        }),
      }),
    );

    expect(screen.getByTestId('kpi-agente-tokens-valor')).toHaveTextContent('6,2 M');
    expect(screen.getByTestId('kpi-agente-tokens-sub')).toHaveTextContent(
      '4,9 M entrada · 1,3 M saída',
    );
  });

  it('tokens por task traz a média e o p95', () => {
    renderGrid(
      agentInsightsFixture({
        tokens: agentTokensFixture({ perTask: { average: 29_800, p95: 61_400 } }),
      }),
    );

    expect(screen.getByTestId('kpi-agente-tokens-por-task-valor')).toHaveTextContent('29,8 mil');
    expect(screen.getByTestId('kpi-agente-tokens-por-task-sub')).toHaveTextContent('p95 61,4 mil');
  });
});

describe('AgentKpiGrid — a taxa de falha e as duas recusas', () => {
  it('a taxa sai de falhas sobre tasks executadas', () => {
    renderGrid(
      agentInsightsFixture({
        volume: agentVolumeFixture({ executedTaskCount: 208 }),
        errors: agentErrorsFixture({ failedCount: 5 }),
      }),
    );

    expect(screen.getByTestId('kpi-agente-falha-valor')).toHaveTextContent('2,4%');
    expect(screen.getByTestId('kpi-agente-falha-sub')).toHaveTextContent(
      '5 falhas · nenhuma recusa',
    );
  });

  it('sem falha, o valor sai por extenso e o subtítulo conta as tasks', () => {
    // `Agente-Delegado.dc.html`: "Nenhuma" no lugar de "0,0%", e "em 65 tasks
    // executadas" no subtítulo.
    renderGrid(
      agentInsightsFixture({
        volume: agentVolumeFixture({ executedTaskCount: 65 }),
        errors: agentErrorsFixture({ failedCount: 0 }),
      }),
    );

    const valor = screen.getByTestId('kpi-agente-falha-valor');
    expect(valor).toHaveTextContent('Nenhuma');
    // O ESTADO continua sendo zero: muda a palavra, não a classificação.
    expect(valor).toHaveAttribute('data-metric-state', 'zero');
    expect(screen.getByTestId('kpi-agente-falha-sub')).toHaveTextContent(
      'em 65 tasks executadas',
    );
  });

  it('o subtítulo NÃO soma as duas recusas', () => {
    // São de regimes diferentes. Somá-las juntaria duas janelas num rótulo só,
    // e é o defeito que o campo separado existe para impedir.
    renderGrid(
      agentInsightsFixture({
        volume: agentVolumeFixture({ executedTaskCount: 100 }),
        errors: agentErrorsFixture({
          failedCount: 4,
          rejectedCount: 3,
          rejectedAtEntryCount: 9,
        }),
      }),
    );

    const sub = screen.getByTestId('kpi-agente-falha-sub');
    expect(sub).toHaveTextContent('4 falhas · 3 recusa');
    expect(sub).not.toHaveTextContent('12');
    expect(sub).not.toHaveTextContent('9');
  });

  it('a recusa de ENTRADA não aparece neste card', () => {
    // Ela tem regime próprio e vive no card de falhas, com o regime declarado.
    renderGrid(
      agentInsightsFixture({
        volume: agentVolumeFixture({ executedTaskCount: 10 }),
        errors: agentErrorsFixture({ failedCount: 1, rejectedAtEntryCount: 7 }),
      }),
    );

    expect(screen.getByTestId('kpi-agente-falha-sub')).not.toHaveTextContent('7');
  });

  it('o caveat de recusa é ÍCONE ao lado do rótulo, e o corpo do card não muda', () => {
    // Ele limita `rejectedCount`, que este subtítulo apresenta — e depois que
    // o grupo de recusa de entrada saiu do card de falhas, este é o ÚNICO
    // lugar da aba onde aquele número aparece.
    renderGrid(agentInsightsFixture({ errors: agentErrorsFixture({ failedCount: 2 }) }));

    const caveat = screen.getByTestId('kpi-agente-falha-caveat');
    expect(caveat).toHaveAttribute('data-caveat-code', 'rejections-missing-from-executions');
    expect(caveat).toHaveAttribute(
      'title',
      expect.stringContaining('não entram no percentual de falha'),
    );
  });

  it('o corpo do card é o do artboard: rótulo, número e subtítulo', () => {
    // O guarda de FORMA. O texto do caveat estava aqui como quarto bloco e foi
    // o que o dono apontou; o ícone não acrescenta linha.
    renderGrid(
      agentInsightsFixture({
        volume: agentVolumeFixture({ executedTaskCount: 208 }),
        errors: agentErrorsFixture({ failedCount: 5 }),
      }),
    );

    const card = screen.getByTestId('kpi-agente-falha');
    expect(card).toHaveTextContent('Taxa de falha');
    expect(card).toHaveTextContent('2,4%');
    expect(card).toHaveTextContent('5 falhas · nenhuma recusa');
    // O texto do caveat NÃO aparece no corpo: ele vive no `title` do ícone.
    expect(card.textContent).not.toContain('não entram no percentual de falha');
  });

  it('sem nenhuma task, a taxa fica VAZIA — não 0%', () => {
    // Um agente que não rodou nada não tem taxa de falha. `0%` afirmaria que
    // ele rodou sem falhar.
    renderGrid(
      agentInsightsFixture({
        volume: agentVolumeFixture({ executedTaskCount: 0 }),
        errors: agentErrorsFixture({ failedCount: 0 }),
      }),
    );

    const valor = screen.getByTestId('kpi-agente-falha-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'empty');
    expect(valor).not.toHaveTextContent('0');
  });
});

describe('AgentKpiGrid — as asserções negativas da gramática', () => {
  it('fonte nula NÃO produz 0 em nenhum dos quatro', () => {
    renderGrid(agentInsightsFixture());

    for (const id of [
      'kpi-agente-tokens-valor',
      'kpi-agente-tokens-por-task-valor',
      'kpi-agente-falha-valor',
    ]) {
      const el = screen.getByTestId(id);
      expect(el).toHaveAttribute('data-metric-state', 'empty');
      expect(el).not.toHaveTextContent('0');
    }
  });

  it('a consulta em curso NÃO produz 0 em lugar nenhum', () => {
    // O defeito que a página do sistema levou doze rodadas para achar: com o
    // estado pendente caindo em "ok", a tela afirmava "Tasks executadas: 0" com
    // a API fora do ar. As contagens `int` do esqueleto são 0 por serem
    // `number`, e é `queryState` que precisa vencer sobre o valor.
    renderGrid(
      agentInsightsFixture({ volume: agentVolumeFixture({ executedTaskCount: 0 }) }),
      'loading',
    );

    const valor = screen.getByTestId('kpi-agente-tasks-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'unknown');
    expect(valor).not.toHaveTextContent('0');
  });

  it('a consulta falhada NÃO produz 0, e traz a razão ao lado do travessão', () => {
    renderGrid(
      agentInsightsFixture({ volume: agentVolumeFixture({ executedTaskCount: 0 }) }),
      'failed',
    );

    const valor = screen.getByTestId('kpi-agente-tasks-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'unknown');
    expect(valor).toHaveAttribute('title', 'A consulta não respondeu.');
    expect(valor).not.toHaveTextContent('0');
  });

  it('o zero MEDIDO não vira célula vazia', () => {
    renderGrid(
      agentInsightsFixture({ volume: agentVolumeFixture({ executedTaskCount: 0 }) }),
    );

    const valor = screen.getByTestId('kpi-agente-tasks-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'zero');
    expect(valor).toHaveTextContent('0');
  });
});
