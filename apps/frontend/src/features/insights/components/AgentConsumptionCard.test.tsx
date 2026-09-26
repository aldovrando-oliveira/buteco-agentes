import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { AgentConsumptionCard } from './AgentConsumptionCard';
import type { AgentFailures, AgentTokens } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';

const ATENDENTE = '55555555-5555-5555-5555-555555555555';
const FANTASMA = '99999999-9999-9999-9999-999999999999';

const byAgent: AgentTokens[] = [
  { agentId: ATENDENTE, inputTokens: 5_000_000, outputTokens: 1_200_000 },
  { agentId: FANTASMA, inputTokens: 900_000, outputTokens: 100_000 },
];

const falhas: AgentFailures[] = [
  { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 5 },
];

function renderCard(
  overrides: Partial<Parameters<typeof AgentConsumptionCard>[0]> = {},
  queryState: QueryState = 'ok',
) {
  const onRetryCatalog = vi.fn();
  render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <AgentConsumptionCard
          byAgent={byAgent}
          failuresByAgent={falhas}
          agentNames={new Map([[ATENDENTE, 'Atendente']])}
          catalogLoading={false}
          catalogFailed={false}
          onRetryCatalog={onRetryCatalog}
          queryState={queryState}
          reason="A consulta não respondeu."
          {...overrides}
        />
      </MemoryRouter>
    </MantineProvider>,
  );
  return onRetryCatalog;
}

describe('AgentConsumptionCard', () => {
  it('cada agente aparece uma vez, com nome e números', () => {
    renderCard();

    expect(screen.getByTestId(`agente-${ATENDENTE}-nome`)).toHaveTextContent('Atendente');
    expect(screen.getByTestId(`agente-${ATENDENTE}-tokens`)).toHaveTextContent('6,2 M');
    expect(screen.getByTestId(`agente-${ATENDENTE}-falhas`)).toHaveTextContent('5');
  });

  it('agente FORA do catálogo mantém a linha, com o identificador no lugar do nome', () => {
    // O consumo dele é real; o que falta é o rótulo. Sumir com a linha faria o
    // total da tabela não fechar com o KPI, sem sintoma nenhum.
    renderCard();

    const nome = screen.getByTestId(`agente-${FANTASMA}-nome`);
    expect(nome).toHaveTextContent('99999999');
    expect(screen.getByTestId(`agente-${FANTASMA}-tokens`)).toHaveTextContent('1 M');
  });

  it('o nome leva DIRETO à aba de Insights do agente, não à visão geral', () => {
    // A #67 fechou pelo caminho (2): tasks, tokens por task e duração p95 por
    // agente ficam SÓ na aba. O argumento que sustenta a decisão é que elas
    // estão a UM clique — então o link tem de abrir a aba, e não o detalhe.
    //
    // Sem o parâmetro, `parseTab(null)` cai em "Visão geral" por contrato
    // declarado (AgentDetailPage), e o clique vira dois.
    renderCard();

    expect(screen.getByTestId(`agente-${ATENDENTE}-nome`)).toHaveAttribute(
      'href',
      `/agents/${ATENDENTE}?tab=insights`,
    );
  });

  it('NEGATIVO: o destino NÃO é o detalhe sem aba, e a aba é a de Insights', () => {
    // O par do caso acima, e ele pega dois defeitos diferentes: o parâmetro
    // sumir (volta a ser dois cliques) e o parâmetro passar a valer OUTRA aba
    // reconhecida por `parseTab` — "ferramentas" abriria uma tela válida, com
    // o link parecendo certo.
    renderCard();

    const href = screen.getByTestId(`agente-${ATENDENTE}-nome`).getAttribute('href') ?? '';

    expect(href).not.toBe(`/agents/${ATENDENTE}`);
    expect(new URL(href, 'http://localhost').searchParams.get('tab')).toBe('insights');
  });

  it('agente sem falhas registradas tem ZERO MEDIDO, e a razão está no código', () => {
    // `errors.byAgent` é um group by sobre as execuções que falharam: o agente
    // está na agregação de tokens (logo foi medido) e não está nas falhas, o
    // que significa que foram contadas e deram zero.
    renderCard();

    const falhasFantasma = screen.getByTestId(`agente-${FANTASMA}-falhas`);
    expect(falhasFantasma).toHaveAttribute('data-metric-state', 'zero');
    // POR EXTENSO, como o `Main.dc.html` escreve — não o algarismo.
    expect(falhasFantasma).toHaveTextContent('Nenhuma');
    expect(falhasFantasma.textContent).not.toContain('0');
  });

  it('o zero por extenso continua sendo o ESTADO de zero, não o de vazio', () => {
    // A palavra muda; a classificação não. Sem isto, "Nenhuma" seria
    // indistinguível de célula vazia para os guardas negativos — e é
    // justamente a distinção que a página defende.
    renderCard();

    const zero = screen.getByTestId(`agente-${FANTASMA}-falhas`);
    expect(zero).toHaveAttribute('data-metric-state', 'zero');
    expect(zero).not.toHaveAttribute('data-metric-state', 'empty');
  });

  it('NEGATIVO: tokens nulos NÃO viram "Nenhuma"', () => {
    // A palavra é reservada ao zero CONTADO. Um agente sem relato de tokens
    // tem célula vazia — "Nenhuma" ali afirmaria que se contou e deu zero.
    renderCard({
      byAgent: [{ agentId: ATENDENTE, inputTokens: null, outputTokens: null }],
    });

    const tokens = screen.getByTestId(`agente-${ATENDENTE}-tokens`);
    expect(tokens).toHaveAttribute('data-metric-state', 'empty');
    expect(tokens.textContent).not.toContain('Nenhuma');
  });

  it('NEGATIVO: tokens com todas as parcelas nulas fica vazio, e não 0', () => {
    renderCard({
      byAgent: [{ agentId: ATENDENTE, inputTokens: null, outputTokens: null }],
    });

    const tokens = screen.getByTestId(`agente-${ATENDENTE}-tokens`);
    expect(tokens).toHaveAttribute('data-metric-state', 'empty');
    expect(tokens.textContent).not.toContain('0');
  });

  it('ordena por tokens, com o desconhecido no fim', () => {
    renderCard();

    const linhas = screen.getAllByTestId(/^agente-[0-9a-f-]+$/);
    expect(linhas[0]).toHaveAttribute('data-testid', `agente-${ATENDENTE}`);
  });

  // ------------------------------------------- O DESTAQUE DA COLUNA FALHAS

  it('a linha de MAIOR contagem de falhas sai em vermelho', () => {
    renderCard({
      failuresByAgent: [
        { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 5 },
        { agentId: FANTASMA, provider: null, model: null, failedCount: 9 },
      ],
    });

    expect(screen.getByTestId(`agente-${FANTASMA}-falhas`)).toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  it('as demais linhas NÃO saem em vermelho', () => {
    renderCard({
      failuresByAgent: [
        { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 5 },
        { agentId: FANTASMA, provider: null, model: null, failedCount: 9 },
      ],
    });

    expect(screen.getByTestId(`agente-${ATENDENTE}-falhas`)).not.toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  it('NEGATIVO: com todas as contagens em zero, NENHUMA linha é destacada', () => {
    // Zero é o maior valor quando não há falha nenhuma, e destacar "Nenhuma"
    // em vermelho afirmaria um problema onde não houve nenhum.
    renderCard({ failuresByAgent: [] });

    for (const id of [ATENDENTE, FANTASMA]) {
      expect(screen.getByTestId(`agente-${id}-falhas`)).not.toHaveStyle({
        color: 'var(--mantine-color-red-filled)',
      });
    }
  });

  it('empate destaca TODOS os empatados', () => {
    // Escolher um seria arbitrário — a tabela não tem critério de desempate,
    // e inventar um (o primeiro, o de mais tokens) afirmaria uma ordem.
    renderCard({
      failuresByAgent: [
        { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 4 },
        { agentId: FANTASMA, provider: null, model: null, failedCount: 4 },
      ],
    });

    for (const id of [ATENDENTE, FANTASMA]) {
      expect(screen.getByTestId(`agente-${id}-falhas`)).toHaveStyle({
        color: 'var(--mantine-color-red-filled)',
      });
    }
  });

  it('o destaque ordena por CONTAGEM, e a limitação está escrita', () => {
    // Documenta o que o destaque NÃO diz: ele ranqueia por contagem absoluta,
    // não por taxa. Aqui o Atendente tem 9 falhas e o outro tem 3 — o
    // destaque vai para o Atendente, mesmo que a taxa dele possa ser menor.
    //
    // A taxa exigiria tasks por agente, que a rota do sistema não serve — e a
    // #67 decidiu que ela NÃO vai entrar. O critério fica como está de
    // propósito: destaque por taxa com a taxa fora da tela seria critério
    // invisível.
    //
    // O cenário do "pior que não leva o destaque" é HIPÓTESE: no dado de 26/09
    // ele não ocorre. O que ocorre é outra coisa, e tem issue — agente sem
    // chamada de provedor não tem linha nenhuma aqui (#84).
    //
    // GATILHO para este caso mudar: o primeiro pedido do dono por taxa de falha
    // no ranking — e aí o que muda primeiro é a POPULAÇÃO da tabela.
    renderCard({
      failuresByAgent: [
        { agentId: ATENDENTE, provider: 'anthropic', model: 'claude-opus-5', failedCount: 9 },
        { agentId: FANTASMA, provider: null, model: null, failedCount: 3 },
      ],
    });

    expect(screen.getByTestId(`agente-${ATENDENTE}-falhas`)).toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  // ------------- AS TRÊS COLUNAS FICAM FORA POR DECISÃO (#67, fechada)
  //
  // Não é lacuna: as três têm fonte, e vivem na aba de Insights do agente. Os
  // guardas abaixo afirmam que a tela não as inventa E que não ganha elemento
  // nenhum para falar da ausência delas.

  it('NEGATIVO: as três colunas saem, e NADA entra no lugar delas', () => {
    renderCard();

    const card = screen.getByTestId('card-consumo-por-agente');
    expect(screen.queryByTestId('agentes-lacuna-colunas')).toBeNull();
    expect(card.querySelector('[data-declared-gap]')).toBeNull();
    // Só as três colunas que TÊM fonte.
    expect(screen.getByTestId('tabela-agentes').querySelectorAll('thead th')).toHaveLength(3);
  });

  it('NEGATIVO: NENHUMA das três colunas aparece preenchida', () => {
    renderCard();

    // Nem cabeçalho, nem valor. O protótipo as desenha; a rota não as serve; e
    // preenchê-las com o número do escopo do agente misturaria níveis de
    // agregação.
    const tabela = screen.getByTestId('tabela-agentes');
    for (const coluna of [/^Tasks$/, /tokens por task/i, /duração/i]) {
      expect(
        screen.queryByRole('columnheader', { name: coluna }),
      ).toBeNull();
    }
    // Só as três colunas que TÊM fonte.
    expect(tabela.querySelectorAll('thead th')).toHaveLength(3);
  });


  // ----------------------------------------- A INDEPENDÊNCIA DAS CONSULTAS

  it('catálogo em carregamento NÃO segura os números', () => {
    renderCard({ agentNames: undefined, catalogLoading: true });

    // Os tokens já estão na tela; só a coluna de nome está carregando.
    expect(screen.getByTestId(`agente-${ATENDENTE}-tokens`)).toHaveTextContent('6,2 M');
    expect(screen.getByTestId(`agente-${ATENDENTE}-carregando`)).toBeInTheDocument();
  });

  it('catálogo que falhou não derruba a tabela, e oferece nova tentativa só para ele', async () => {
    const onRetryCatalog = renderCard({ agentNames: undefined, catalogFailed: true });

    expect(screen.getByTestId(`agente-${ATENDENTE}-tokens`)).toHaveTextContent('6,2 M');
    expect(screen.getByTestId('catalogo-falhou')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }));
    expect(onRetryCatalog).toHaveBeenCalledTimes(1);
  });

  it('sem catálogo, o identificador aparece para todos, e as linhas permanecem', () => {
    renderCard({ agentNames: undefined, catalogFailed: true });

    expect(screen.getAllByTestId(/^agente-[0-9a-f-]+$/)).toHaveLength(2);
    expect(screen.getByTestId(`agente-${ATENDENTE}-nome`)).toHaveTextContent('55555555');
  });

  it('sem nenhum agente, diz o zero medido por extenso', () => {
    renderCard({ byAgent: [] });

    expect(screen.getByTestId('consumo-por-agente-vazio')).toHaveTextContent(
      'Nenhum consumo por agente neste período.',
    );
  });
});
