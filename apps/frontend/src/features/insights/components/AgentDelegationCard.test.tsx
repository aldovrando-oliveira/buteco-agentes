import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentDelegationCard } from './AgentDelegationCard';
import type { AgentDelegationInsights } from '../types/agentInsights';
import type { DelegationCatalogAgent } from '../utils/delegationRows';
import type { QueryState } from '../utils/metricState';
import {
  AGENT_ID,
  SOURCE_ID,
  TARGET_ID,
  agentDelegationFixture,
  delegatesOnlyFixture,
  divergentDelegationFixture,
  mixedDelegationFixture,
  noDelegationFixture,
  triggeredOnlyFixture,
} from '../test/agentInsightsFixture';

const CATALOGO: DelegationCatalogAgent[] = [
  { id: AGENT_ID, name: 'Atendente', delegatesTo: [] },
  { id: TARGET_ID, name: 'Cobrança', delegatesTo: [] },
  { id: SOURCE_ID, name: 'Triagem', delegatesTo: [{ id: AGENT_ID, name: 'Atendente' }] },
];

function renderCard(
  delegation: AgentDelegationInsights,
  {
    registeredTargets = [] as { id: string; name: string }[],
    catalog = CATALOGO as DelegationCatalogAgent[] | undefined,
    // Bandeira PRÓPRIA, e não `catalog: undefined`: um parâmetro com valor
    // padrão trata `undefined` como "não informado" e reinstala o default,
    // então o caso "sem catálogo" testaria o contrário do que diz.
    semCatalogo = false,
    externalOriginTaskCount = 0,
    queryState = 'ok' as QueryState,
  } = {},
) {
  const { container } = render(
    <MantineProvider theme={theme}>
      <AgentDelegationCard
        delegation={delegation}
        agentId={AGENT_ID}
        registeredTargets={registeredTargets}
        catalog={semCatalogo ? undefined : catalog}
        externalOriginTaskCount={externalOriginTaskCount}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
  return container;
}

describe('AgentDelegationCard — a assimetria, que é a razão desta aba existir', () => {
  it('os dois lados DIVERGEM, e a tela afirma os dois sem reconciliar', () => {
    // 30 tentativas (24 concluídas + 6 nunca iniciadas) contra 24 execuções do
    // outro lado. É o guarda que AFIRMA a divergência — um que afirmasse
    // igualdade reprovaria o comportamento correto (convenção 15).
    renderCard(divergentDelegationFixture(), {
      registeredTargets: [{ id: TARGET_ID, name: 'Cobrança' }],
    });

    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveTextContent('30');
    expect(screen.getByTestId(`acionado-por-${TARGET_ID}-valor`)).toHaveTextContent('24');
  });

  it('nenhum aviso de inconsistência é renderizado quando os lados discordam', () => {
    const container = renderCard(divergentDelegationFixture(), {
      registeredTargets: [{ id: TARGET_ID, name: 'Cobrança' }],
    });

    // Um alerta acusa; o `caveat` explica. Transformar resultado correto em
    // defeito aparente é exatamente o que a change B existe para impedir.
    expect(container.querySelector('[role="alert"]')).toBeNull();
    const card = screen.getByTestId('card-delegacao-do-agente');
    expect(card).not.toHaveTextContent(/inconsist|diverg[êe]ncia detectada|erro|atenção/i);
  });

  it('NENHUM total soma os dois lados', () => {
    renderCard(divergentDelegationFixture(), {
      registeredTargets: [{ id: TARGET_ID, name: 'Cobrança' }],
    });

    // 30 + 24 = 54. Um total assim apareceria no dia em que alguém achasse que
    // "fica mais completo", e é a soma que a tela não pode fazer.
    expect(screen.getByTestId('card-delegacao-do-agente')).not.toHaveTextContent('54');
  });

  it('quando os dois lados COINCIDEM, a tela continua com dois conjuntos', () => {
    // Bater é possível e não é garantido. A tela não pode virar "uma relação
    // vista de dois ângulos" só porque os números bateram nesta janela.
    renderCard(
      agentDelegationFixture({
        delegatesTo: [{ targetAgentId: TARGET_ID, outcome: 'Completed', count: 23 }],
        triggeredBy: [{ sourceAgentId: TARGET_ID, executedCount: 23 }],
      }),
    );

    expect(screen.getByTestId('secao-delega-para')).toBeInTheDocument();
    expect(screen.getByTestId('secao-acionado-por')).toBeInTheDocument();
    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveTextContent('23');
    expect(screen.getByTestId(`acionado-por-${TARGET_ID}-valor`)).toHaveTextContent('23');
  });

  it('o caveat da assimetria é ÍCONE VISÍVEL no card, com o texto inteiro alcançável', () => {
    // Ele saiu do rodapé em prosa na conferência manual de 26/09 e virou ícone
    // no cabeçalho. O que não podia mudar: um código que a rota manda, cujo
    // número está nesta superfície, NÃO pode ser descartado em silêncio.
    renderCard(divergentDelegationFixture());

    const caveat = screen.getByTestId('delegacao-caveat');
    expect(caveat).toHaveAttribute('data-caveat-code', 'delegation-sides-are-not-mirrors');
    // O `title` é o que sobrevive à leitura por leitor de tela, e é ele que
    // carrega o texto INTEIRO — o mesmo idioma de `MetricValue`.
    expect(caveat).toHaveAttribute(
      'title',
      expect.stringContaining('um conta tentativa, o outro conta execução'),
    );
    expect(caveat).toHaveAttribute('aria-label', expect.stringContaining('relógios diferentes'));
  });

  it('o card NÃO tem mais o caveat como parágrafo no rodapé', () => {
    const container = renderCard(divergentDelegationFixture());

    // O texto não aparece no corpo do card: ele vive no `title` do ícone e no
    // tooltip. Se alguém devolvê-lo como parágrafo, este caso aponta.
    const paragrafos = [...container.querySelectorAll('p')].map((e) => e.textContent ?? '');
    expect(paragrafos.join(' ')).not.toContain('um conta tentativa');
  });
});

describe('AgentDelegationCard — o resultado discriminado (D2)', () => {
  it('cada resultado aparece nomeado com a sua contagem', () => {
    renderCard(divergentDelegationFixture());

    expect(screen.getByTestId(`delega-para-${TARGET_ID}-resultados`)).toHaveTextContent(
      'Concluída 24 · Não iniciada 6',
    );
  });

  it('a tela NÃO apresenta apenas a soma dos resultados', () => {
    renderCard(divergentDelegationFixture());

    // O total continua lá — é o ranking do artboard —, mas não sozinho: é o
    // resultado que explica a divergência com o outro lado.
    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveTextContent('30');
    expect(screen.getByTestId(`delega-para-${TARGET_ID}-resultados`)).toBeInTheDocument();
  });

  it('resultado desconhecido aparece CRU', () => {
    renderCard(
      agentDelegationFixture({
        delegatesTo: [{ targetAgentId: TARGET_ID, outcome: 'Rescheduled', count: 3 }],
      }),
    );

    const resultados = screen.getByTestId(`delega-para-${TARGET_ID}-resultados`);
    expect(resultados).toHaveTextContent('Rescheduled 3');
    expect(resultados).not.toHaveTextContent(/Concluída|Expirou|Não iniciada/);
  });

  it('vínculo cadastrado e ocioso não tem linha de resultado nenhuma', () => {
    renderCard(noDelegationFixture(), {
      registeredTargets: [{ id: TARGET_ID, name: 'Cobrança' }],
    });

    expect(
      screen.queryByTestId(`delega-para-${TARGET_ID}-resultados`),
    ).not.toBeInTheDocument();
  });
});

describe('AgentDelegationCard — os quatro cenários', () => {
  it('cenário 1, só delega: a outra seção aparece tracejada, não some', () => {
    renderCard(delegatesOnlyFixture(), {
      registeredTargets: [{ id: TARGET_ID, name: 'Cobrança' }],
      catalog: [CATALOGO[0], CATALOGO[1]],
      externalOriginTaskCount: 208,
    });

    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveTextContent('27');
    expect(screen.getByTestId('acionado-por-sem-vinculo')).toBeInTheDocument();
    expect(screen.getByTestId('secao-acionado-por')).toBeInTheDocument();
  });

  it('cenário 2, só é delegado: idem do outro lado', () => {
    renderCard(triggeredOnlyFixture(), { externalOriginTaskCount: 0 });

    expect(screen.getByTestId(`acionado-por-${SOURCE_ID}-valor`)).toHaveTextContent('51');
    expect(screen.getByTestId('delega-para-sem-vinculo')).toHaveTextContent(
      'Sem delegação cadastrada.',
    );
  });

  it('cenário 3, os dois lados, e nenhum é derivado do outro', () => {
    renderCard(mixedDelegationFixture(), {
      registeredTargets: [{ id: TARGET_ID, name: 'Cobrança' }],
    });

    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveTextContent('51');
    expect(screen.getByTestId(`acionado-por-${SOURCE_ID}-valor`)).toHaveTextContent('70');
    expect(screen.queryByTestId('delega-para-sem-vinculo')).not.toBeInTheDocument();
    expect(screen.queryByTestId('acionado-por-sem-vinculo')).not.toBeInTheDocument();
  });

  it('cenário 4, nenhum dos dois: o MESMO card, com as duas seções tracejadas', () => {
    // Não tem artboard. Vem da nota `cenarios` do `canvas.json`: "o mesmo card
    // com as duas seções tracejadas".
    const container = renderCard(noDelegationFixture(), { catalog: [CATALOGO[0]] });

    expect(screen.getByTestId('card-delegacao-do-agente')).toBeInTheDocument();
    expect(screen.getByTestId('delega-para-sem-vinculo')).toBeInTheDocument();
    expect(screen.getByTestId('acionado-por-sem-vinculo')).toBeInTheDocument();
    expect(container.querySelectorAll('[data-no-binding]')).toHaveLength(2);
    // NENHUMA contagem zero no lugar delas.
    expect(container.querySelectorAll('[data-delegation-row]')).toHaveLength(0);
    expect(screen.getByTestId('delega-para-sem-vinculo')).not.toHaveTextContent('0');
  });
});

describe('AgentDelegationCard — cadastro e uso são fatos diferentes (D14)', () => {
  it('vínculo cadastrado e ocioso vira linha com 0, NÃO tracejado', () => {
    renderCard(noDelegationFixture(), {
      registeredTargets: [{ id: TARGET_ID, name: 'Cobrança' }],
    });

    const valor = screen.getByTestId(`delega-para-${TARGET_ID}-valor`);
    expect(valor).toHaveTextContent('0');
    expect(valor).toHaveAttribute('data-metric-state', 'zero');
    expect(screen.queryByTestId('delega-para-sem-vinculo')).not.toBeInTheDocument();
  });

  it('ausência de cadastro NÃO produz nenhum 0', () => {
    const container = renderCard(noDelegationFixture(), { catalog: [CATALOGO[0]] });

    expect(screen.getByTestId('delega-para-sem-vinculo')).toBeInTheDocument();
    expect(container.querySelectorAll('[data-metric-state]')).toHaveLength(0);
  });

  it('ocorrência medida sem cadastro atual continua visível, e diz por quê', () => {
    renderCard(
      agentDelegationFixture({
        delegatesTo: [{ targetAgentId: TARGET_ID, outcome: 'Completed', count: 9 }],
      }),
      { registeredTargets: [] },
    );

    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveTextContent('9');
    expect(screen.getByTestId(`delega-para-${TARGET_ID}-sem-cadastro`)).toHaveTextContent(
      'A medição aconteceu',
    );
  });

  it('a distinção é feita pelo DESENHO, sem parágrafo explicativo', () => {
    // A nota em prosa saiu na conferência manual de 26/09. O que a substitui
    // não é outro texto: é a forma. Vínculo ocioso é uma LINHA com `0`;
    // ausência de cadastro é o quadro TRACEJADO, sem número.
    // Catálogo SEM a `Triagem`, que declara este agente como destino: assim o
    // lado de entrada fica sem cadastro e as duas formas aparecem juntas.
    const comOcioso = renderCard(noDelegationFixture(), {
      registeredTargets: [{ id: TARGET_ID, name: 'Cobrança' }],
      catalog: [CATALOGO[0], CATALOGO[1]],
    });

    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveAttribute(
      'data-metric-state',
      'zero',
    );
    expect(comOcioso.querySelectorAll('[data-no-binding]')).toHaveLength(1);
    expect(screen.queryByTestId('nota-cadastro-e-uso')).not.toBeInTheDocument();
    expect(screen.getByTestId('card-delegacao-do-agente')).not.toHaveTextContent(
      /As duas seções existem em todo agente/i,
    );
  });
});

describe('AgentDelegationCard — o texto do tracejado de entrada é condicional', () => {
  it('COM origem externa, afirma que o agente recebe pedidos externos', () => {
    renderCard(delegatesOnlyFixture(), {
      catalog: [CATALOGO[0], CATALOGO[1]],
      externalOriginTaskCount: 208,
    });

    expect(screen.getByTestId('acionado-por-sem-vinculo')).toHaveTextContent(
      'Nenhum agente aciona este. Ele recebe pedidos externos.',
    );
  });

  it('SEM origem externa, NÃO afirma origem que não houve', () => {
    // O artboard escreve as duas frases juntas. No quarto cenário a segunda
    // afirmaria uma origem que não houve.
    renderCard(noDelegationFixture(), {
      catalog: [CATALOGO[0]],
      externalOriginTaskCount: 0,
    });

    const tracejado = screen.getByTestId('acionado-por-sem-vinculo');
    expect(tracejado).toHaveTextContent('Nenhum agente aciona este.');
    expect(tracejado).not.toHaveTextContent('pedidos externos');
  });
});

describe('AgentDelegationCard — o catálogo e os estados de consulta', () => {
  it('sem catálogo, a linha fica com o identificador e NÃO some', () => {
    renderCard(divergentDelegationFixture(), { semCatalogo: true });

    expect(screen.getByTestId(`delega-para-${TARGET_ID}-nome`)).toHaveTextContent(TARGET_ID);
    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveTextContent('30');
  });

  it('a consulta em curso NÃO produz 0 nem barra', () => {
    const container = renderCard(divergentDelegationFixture(), { queryState: 'loading' });

    const valor = screen.getByTestId(`delega-para-${TARGET_ID}-valor`);
    expect(valor).toHaveAttribute('data-metric-state', 'unknown');
    expect(valor).not.toHaveTextContent('0');
    // Barra desenhada com a consulta pendente afirmaria proporção sobre o
    // esqueleto.
    expect(container.querySelectorAll('[data-delegation-bar]')).toHaveLength(0);
  });

  it('a consulta falhada traz a razão ao lado do travessão', () => {
    renderCard(divergentDelegationFixture(), { queryState: 'failed' });

    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveAttribute(
      'title',
      'A consulta não respondeu.',
    );
  });

  it('a consulta falhada esconde o resultado discriminado', () => {
    // Os resultados vêm do mesmo corpo que não chegou: exibi-los afirmaria
    // uma repartição sobre o esqueleto.
    renderCard(divergentDelegationFixture(), { queryState: 'failed' });

    expect(
      screen.queryByTestId(`delega-para-${TARGET_ID}-resultados`),
    ).not.toBeInTheDocument();
  });
});
