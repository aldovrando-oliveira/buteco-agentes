import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentFailuresCard } from './AgentFailuresCard';
import { agentErrorsFixture } from '../test/agentInsightsFixture';
import type { AgentErrorInsights } from '../types/agentInsights';
import type { QueryState } from '../utils/metricState';

function renderCard(
  errors: AgentErrorInsights,
  { executedTaskCount = 65, queryState = 'ok' as QueryState } = {},
) {
  const { container } = render(
    <MantineProvider theme={theme}>
      <AgentFailuresCard
        errors={errors}
        executedTaskCount={executedTaskCount}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
  return container;
}

const comFalhas = agentErrorsFixture({
  failedCount: 5,
  byPhase: [
    { phase: 'AgentRun', count: 3 },
    { phase: 'ToolResolution', count: 2 },
  ],
  byProviderAndModel: [{ provider: 'anthropic', model: 'claude-opus-5', failedCount: 5 }],
});

describe('AgentFailuresCard — o título tem os DOIS estados do artboard', () => {
  it('com falha, é "Por que as falhas aconteceram"', () => {
    // `Agente-Insights.dc.html` escreve "Por que as 5 falhas aconteceram". A
    // contagem saiu do título e foi para a direita do cabeçalho, na mesma
    // posição do total em "Onde o tempo foi".
    renderCard(comFalhas);

    expect(screen.getByTestId('card-falhas-do-agente')).toHaveTextContent(
      'Por que as falhas aconteceram',
    );
    // A contagem NÃO fica mais embutida na frase.
    expect(screen.getByTestId('card-falhas-do-agente')).not.toHaveTextContent(
      'Por que as 5 falhas',
    );
  });

  it('sem falha, é só "Falhas" — e isso é o artboard, não simplificação', () => {
    // `Agente-Delegado.dc.html`, o agente sem falha, titula o card "Falhas".
    renderCard(agentErrorsFixture({ failedCount: 0 }));

    const card = screen.getByTestId('card-falhas-do-agente');
    expect(card).toHaveTextContent('Falhas');
    expect(card).not.toHaveTextContent('Por que as falhas aconteceram');
  });

  it('a consulta sem resposta usa o título neutro, não o de "sem falha"', () => {
    // Requisição que não respondeu não é evidência de ausência.
    renderCard(comFalhas, { queryState: 'loading' });

    expect(screen.getByTestId('card-falhas-do-agente')).not.toHaveTextContent(
      'Por que as falhas aconteceram',
    );
  });

  it('a contagem fica à DIREITA do cabeçalho, como em "Onde o tempo foi"', () => {
    renderCard(comFalhas);

    expect(screen.getByTestId('falhas-total')).toHaveTextContent('total de falhas');
    expect(screen.getByTestId('falhas-total-valor')).toHaveTextContent('5');
  });

  it('a contagem no cabeçalho segue a gramática', () => {
    renderCard(agentErrorsFixture({ failedCount: 0 }));
    expect(screen.getByTestId('falhas-total-valor')).toHaveAttribute(
      'data-metric-state',
      'zero',
    );
  });
});

describe('AgentFailuresCard — os grupos que substituem a tabela única (D9)', () => {
  it('fase e provedor/modelo saem em grupos SEPARADOS, nunca cruzados', () => {
    renderCard(comFalhas);

    expect(screen.getByTestId('grupo-por-fase')).toBeInTheDocument();
    expect(screen.getByTestId('grupo-por-provedor-e-modelo')).toBeInTheDocument();
    expect(screen.getByTestId('fase-AgentRun-valor')).toHaveTextContent('3');
    expect(screen.getByTestId('provedor-modelo-0-valor')).toHaveTextContent('5');
  });

  it('a linha de fase NÃO carrega provedor nem modelo', () => {
    // Nenhum campo junta fase a provedor/modelo. Cruzá-las no cliente
    // inventaria a junção, com número plausível e sem medição por trás.
    renderCard(comFalhas);

    expect(screen.getByTestId('fase-AgentRun')).not.toHaveTextContent('claude-opus-5');
    expect(screen.getByTestId('fase-AgentRun')).not.toHaveTextContent('anthropic');
  });

  it('NENHUMA coluna de servidor MCP é renderizada', () => {
    renderCard(comFalhas);

    expect(screen.getByTestId('card-falhas-do-agente')).not.toHaveTextContent(/MCP/i);
  });

  it('fase desconhecida aparece CRUA', () => {
    renderCard(
      agentErrorsFixture({ failedCount: 1, byPhase: [{ phase: 'NovaFase', count: 1 }] }),
    );

    const linha = screen.getByTestId('fase-NovaFase');
    expect(linha).toHaveTextContent('NovaFase');
    expect(linha).toHaveAttribute('data-raw-value', 'true');
  });

  it('provedor e modelo nulos dizem que não foram registrados', () => {
    // A execução pode ter falhado ANTES de resolver qualquer um dos dois.
    renderCard(
      agentErrorsFixture({
        failedCount: 2,
        byProviderAndModel: [{ provider: null, model: null, failedCount: 2 }],
      }),
    );

    const linha = screen.getByTestId('provedor-modelo-0');
    expect(linha).toHaveTextContent('modelo não registrado');
    expect(linha).toHaveTextContent('provedor não registrado');
  });
});

describe('AgentFailuresCard — a recusa de entrada NÃO tem elemento neste card', () => {
  it('nem a contagem, nem os motivos, nem o regime aparecem', () => {
    // A D6 tinha criado um grupo para eles. O artboard não tem elemento
    // nenhum, e a D10 da mesma change proíbe criar um para métrica servida e
    // não desenhada. As duas se contradiziam, e o dono viu na tela: o rodapé
    // pesava mais que a tabela de falhas.
    const container = renderCard(
      agentErrorsFixture({
        failedCount: 5,
        rejectedCount: 2,
        rejectedAtEntryCount: 9,
        byPhase: [{ phase: 'AgentRun', count: 5 }],
        rejectionsByReason: [{ reason: 'AgentInactive', count: 9 }],
      }),
    );

    expect(screen.queryByTestId('grupo-recusa-de-entrada')).not.toBeInTheDocument();
    expect(screen.queryByTestId('recusa-de-entrada-total')).not.toBeInTheDocument();
    const texto = container.textContent ?? '';
    expect(texto).not.toContain('Recusadas antes de executar');
    expect(texto).not.toContain('Agente inativo');
    expect(texto).not.toContain('regime próprio');
    // E a contagem dela não vaza para nenhum número do card.
    expect(screen.getByTestId('falhas-total-valor')).toHaveTextContent('5');
  });

  it('o caveat de recusa também não fica aqui — ele vive no KPI', () => {
    // Ele limita `rejectedCount`, e o único lugar da aba onde esse número
    // aparece é o subtítulo do KPI `Taxa de falha`. A presença lá é afirmada
    // por `AgentKpiGrid.test.tsx`.
    const container = renderCard(comFalhas);

    expect(screen.queryByTestId('falhas-do-agente-caveat')).not.toBeInTheDocument();
    expect(container.textContent ?? '').not.toContain('não entram no percentual de falha');
  });

  it('o card tem SÓ cabeçalho e conteúdo — nenhum rodapé', () => {
    const container = renderCard(comFalhas);

    // O guarda de forma que o dono pediu: o que sobrou é a tabela.
    const textos = [...container.querySelectorAll('p')].map((e) => e.textContent ?? '');
    expect(textos.some((t) => t.includes('medido desde'))).toBe(false);
    expect(textos.some((t) => t.includes('não somável'))).toBe(false);
  });
});

describe('AgentFailuresCard — período sem falha', () => {
  it('usa o quadro tracejado com o total, e NÃO tabela vazia', () => {
    // O quadro tracejado É do artboard — `Agente-Delegado.dc.html`.
    renderCard(agentErrorsFixture({ failedCount: 0 }), { executedTaskCount: 65 });

    expect(screen.getByTestId('falhas-do-agente-sem-falha')).toHaveTextContent(
      'Nenhuma falha no período. As 65 tasks chegaram a concluído.',
    );
    expect(screen.queryByTestId('grupo-por-fase')).not.toBeInTheDocument();
    expect(screen.queryByTestId('grupo-por-provedor-e-modelo')).not.toBeInTheDocument();
  });

  it('a consulta em curso NÃO produz o quadro de "nenhuma falha"', () => {
    renderCard(agentErrorsFixture({ failedCount: 0 }), { queryState: 'loading' });

    expect(screen.queryByTestId('falhas-do-agente-sem-falha')).not.toBeInTheDocument();
    const total = screen.getByTestId('falhas-total-valor');
    expect(total).toHaveAttribute('data-metric-state', 'unknown');
    expect(total).not.toHaveTextContent('0');
  });

  it('a consulta falhada traz a razão ao lado do travessão', () => {
    renderCard(comFalhas, { queryState: 'failed' });

    expect(screen.getByTestId('falhas-total-valor')).toHaveAttribute(
      'title',
      'A consulta não respondeu.',
    );
  });
});
