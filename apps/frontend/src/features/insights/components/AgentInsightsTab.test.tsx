import { readFileSync } from 'node:fs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useState, type ReactNode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { theme } from '../../../theme';
import { AgentInsightsTab } from './AgentInsightsTab';
import { ApiError, getAgentInsights } from '../api/insightsApi';
import {
  AGENT_ID,
  SOURCE_ID,
  TARGET_ID,
  agentDelegationFixture,
  agentErrorsFixture,
  agentInsightsFixture,
  agentPerformanceFixture,
  agentTokensFixture,
  agentVolumeFixture,
  divergentDelegationFixture,
} from '../test/agentInsightsFixture';
import type { DelegationCatalogAgent } from '../utils/delegationRows';
import { DEFAULT_INSIGHTS_PERIOD, type InsightsPeriod } from '../utils/insightsWindow';

vi.mock('../api/insightsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/insightsApi')>();
  return { ...actual, getAgentInsights: vi.fn() };
});

const CATALOGO: DelegationCatalogAgent[] = [
  { id: AGENT_ID, name: 'Atendente', delegatesTo: [] },
  { id: TARGET_ID, name: 'Cobrança', delegatesTo: [] },
  { id: SOURCE_ID, name: 'Triagem', delegatesTo: [] },
];

function Wrapper({ children }: { children: ReactNode }) {
  // `retryDelay: 0` porque a POLÍTICA de retentativa é do hook e vence a do
  // cliente: para tudo que não é `404` ele repete três vezes, como o resto do
  // painel. O que o cliente de teste ainda controla é o INTERVALO, e sem
  // zerá-lo o caso da falha espera o backoff exponencial em vez de medir a
  // tela.
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, retryDelay: 0 } },
  });
  return (
    <QueryClientProvider client={client}>
      <MantineProvider theme={theme}>{children}</MantineProvider>
    </QueryClientProvider>
  );
}

/**
 * O ARRANJO É CONTROLADO, E ISSO É CONSEQUÊNCIA DA #85.
 *
 * O período deixou de ser estado desta aba e passou a ser prop da página (D4).
 * Um `vi.fn()` em `onPeriodChange` deixaria o seletor sem efeito — clicar em "7d"
 * chamaria o espião e a prop continuaria a mesma —, e o caso "trocar o período
 * consulta de novo" passaria a medir nada. Este invólucro mínimo devolve à aba o
 * comportamento que a página real lhe dá, sem trazer router nem endereço para
 * dentro de um teste de componente.
 *
 * O PERÍODO INICIAL É `90d`, e não o padrão do sistema: se fosse `30d`, um
 * `DEFAULT_INSIGHTS_PERIOD` fixado à mão dentro da aba passaria por todos os casos
 * deste arquivo.
 */
const PERIODO_DO_ARRANJO: InsightsPeriod = '90d';

function TabControlada({
  registeredTargets,
  agentsCatalog,
  periodoInicial,
  onPeriodChange,
}: {
  registeredTargets: { id: string; name: string }[];
  agentsCatalog: DelegationCatalogAgent[] | undefined;
  periodoInicial: InsightsPeriod;
  onPeriodChange?: (period: InsightsPeriod) => void;
}) {
  const [period, setPeriod] = useState<InsightsPeriod>(periodoInicial);
  return (
    <AgentInsightsTab
      agentId={AGENT_ID}
      registeredTargets={registeredTargets}
      agentsCatalog={agentsCatalog}
      period={period}
      onPeriodChange={(next) => {
        onPeriodChange?.(next);
        setPeriod(next);
      }}
    />
  );
}

function renderTab({
  registeredTargets = [] as { id: string; name: string }[],
  agentsCatalog = CATALOGO as DelegationCatalogAgent[] | undefined,
  // Bandeira PRÓPRIA, e não `agentsCatalog: undefined`: parâmetro com valor
  // padrão trata `undefined` como "não informado" e reinstala o default, então
  // o caso "sem catálogo" testaria o contrário do que diz. É a mesma armadilha
  // que `AgentDelegationCard.test.tsx` já documenta.
  semCatalogo = false,
  periodoInicial = PERIODO_DO_ARRANJO as InsightsPeriod,
  onPeriodChange = undefined as ((period: InsightsPeriod) => void) | undefined,
} = {}) {
  return render(
    <Wrapper>
      <TabControlada
        registeredTargets={registeredTargets}
        agentsCatalog={semCatalogo ? undefined : agentsCatalog}
        periodoInicial={periodoInicial}
        onPeriodChange={onPeriodChange}
      />
    </Wrapper>,
  );
}

/**
 * Espera a RESPOSTA, não a montagem.
 *
 * `findByTestId` resolve durante o carregamento: os cards existem desde o
 * primeiro render, sustentados pelo esqueleto, e é justamente isso que a
 * gramática exige. Afirmar um valor logo após `findByTestId` mede o estado
 * pendente — seis casos desta suíte reprovaram assim na primeira escrita.
 */
async function aguardarResposta() {
  await waitFor(() =>
    expect(screen.getByTestId('kpi-agente-tasks-valor')).not.toHaveAttribute(
      'data-metric-state',
      'unknown',
    ),
  );
}

const povoado = agentInsightsFixture({
  volume: agentVolumeFixture({
    executedTaskCount: 208,
    externalOriginTaskCount: 208,
    delegationOriginTaskCount: 0,
  }),
  tokens: agentTokensFixture({
    conversation: { inputTokens: 4_900_000, outputTokens: 1_300_000, cachedInputTokens: null },
    byModel: [
      { provider: 'anthropic', model: 'claude-opus-5', totalTokens: 5_000_000, callCount: 142 },
    ],
    perTask: { average: 29_800, p95: 61_400 },
  }),
  performance: agentPerformanceFixture({
    taskDuration: { averageMs: 4400, p95Ms: 8400, sampleCount: 208 },
    queueTime: { averageMs: 300, p95Ms: 500, sampleCount: 208 },
    providerCallDuration: { averageMs: 3200, p95Ms: 5100, sampleCount: 238 },
    nonProviderResidual: { averageMs: 900, p95Ms: 1400, sampleCount: 205 },
    providerCallsPerTask: 1.1,
  }),
  errors: agentErrorsFixture({ failedCount: 5, byPhase: [{ phase: 'AgentRun', count: 5 }] }),
  delegation: divergentDelegationFixture(),
  temporal: {
    regime: 'execution',
    dailySeries: [
      { day: '2026-09-22', taskCount: 26, tokenCount: 900_000 },
      { day: '2026-09-23', taskCount: 0, tokenCount: null },
      { day: '2026-09-24', taskCount: 31, tokenCount: 1_100_000 },
    ],
    byWeekday: [{ weekday: 2, taskCount: 26 }],
    peakWeekday: 2,
  },
});

describe('AgentInsightsTab — a consulta', () => {
  beforeEach(() => {
    vi.mocked(getAgentInsights).mockReset();
    vi.mocked(getAgentInsights).mockResolvedValue(povoado);
  });

  it('faz UMA requisição por abertura, com o id do agente', async () => {
    renderTab();

    await screen.findByTestId('aba-insights-do-agente');
    expect(getAgentInsights).toHaveBeenCalledTimes(1);
    expect(vi.mocked(getAgentInsights).mock.calls[0][0]).toBe(AGENT_ID);
  });

  it('trocar o período consulta de novo', async () => {
    renderTab();
    await screen.findByTestId('kpis-do-agente');

    await userEvent.click(screen.getByRole('radio', { name: '7d' }));

    await waitFor(() => expect(getAgentInsights).toHaveBeenCalledTimes(2));
  });

  it('a aba consulta a janela do período RECEBIDO, não a do padrão', async () => {
    // O defeito que a #85 fecha, visto do lado de dentro: a aba mantinha o período
    // em `useState(DEFAULT_INSIGHTS_PERIOD)`, então quem chegava aqui pelo ranking
    // em 90 dias era medido em 30 — e os dois números estavam certos, sobre
    // períodos diferentes.
    //
    // A LARGURA, e não os instantes (D8): `(to - from)` não depende de quando o
    // teste roda, então não há relógio a controlar. A feature não tem `TimeProvider`
    // nem usa `vi.setSystemTime` em teste nenhum, e esta decisão é o que a mantém
    // assim.
    renderTab({ periodoInicial: '7d' });

    await aguardarResposta();

    const [, from, to] = vi.mocked(getAgentInsights).mock.calls[0];
    const dias = (new Date(to).getTime() - new Date(from).getTime()) / (24 * 60 * 60 * 1000);
    expect(dias).toBe(7);
    expect(screen.getByRole('radio', { name: '7d' })).toBeChecked();
  });

  it('a troca de período é ENTREGUE à página, que é quem escreve o endereço', async () => {
    // A aba não escreve o endereço — ela avisa quem o possui (D4). Sem este
    // guarda, a aba poderia voltar a guardar o período em estado local e os casos
    // acima continuariam verdes, porque o invólucro deste arquivo faz o papel da
    // página.
    const onPeriodChange = vi.fn();
    renderTab({ periodoInicial: '90d', onPeriodChange });
    await screen.findByTestId('kpis-do-agente');

    await userEvent.click(screen.getByRole('radio', { name: '7d' }));

    expect(onPeriodChange).toHaveBeenCalledWith('7d');
    expect(onPeriodChange).not.toHaveBeenCalledWith(DEFAULT_INSIGHTS_PERIOD);
  });

  it('a janela do cabeçalho é a que a RESPOSTA ecoa', async () => {
    renderTab();

    // A resposta é a autoridade sobre o que foi consultado; o cálculo local só
    // vale enquanto ela não chegou.
    await aguardarResposta();
    const janela = screen.getByTestId('janela-do-periodo-do-agente');
    expect(janela).toHaveTextContent('15/09/2026 a 24/09/2026');
    expect(janela).toHaveTextContent('America/Sao_Paulo');
    expect(janela).toHaveTextContent('execução medida desde 22/09/2026');
  });

  it('os sete cards do artboard aparecem', async () => {
    renderTab();

    await screen.findByTestId('kpis-do-agente');
    for (const id of [
      'kpis-do-agente',
      'card-onde-o-tempo-foi',
      'card-duracao-da-task',
      'card-modelos-do-agente',
      'card-dias-da-semana',
      'card-delegacao-do-agente',
      'card-falhas-do-agente',
    ]) {
      expect(screen.getByTestId(id)).toBeInTheDocument();
    }
  });

  it('o que a rota serve e o artboard não desenha NÃO aparece (D10)', async () => {
    renderTab();
    await screen.findByTestId('kpis-do-agente');

    // Mapa de calor, série diária, banner de não-terminais, card de embedding
    // e consumo por provedor: quatro conjuntos servidos que ficam de fora,
    // cada um registrado com gatilho.
    expect(screen.queryByTestId('card-calendario')).not.toBeInTheDocument();
    expect(screen.queryByTestId('card-tasks-por-dia')).not.toBeInTheDocument();
    expect(screen.queryByTestId('banner-nao-terminais')).not.toBeInTheDocument();
    expect(screen.queryByTestId('card-consumo-por-provedor')).not.toBeInTheDocument();
  });
});

describe('AgentInsightsTab — os quatro estados de resposta', () => {
  beforeEach(() => {
    vi.mocked(getAgentInsights).mockReset();
  });

  it('a consulta em curso NÃO produz 0 em lugar nenhum', async () => {
    // O defeito que a página do sistema levou doze rodadas para achar: com o
    // estado pendente caindo em "ok", a tela afirmava "Tasks executadas: 0"
    // com a API fora do ar.
    vi.mocked(getAgentInsights).mockReturnValue(new Promise(() => {}));
    const { container } = renderTab();

    const valor = await screen.findByTestId('kpi-agente-tasks-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'unknown');
    for (const el of container.querySelectorAll('[data-metric-state]')) {
      expect(el.getAttribute('data-metric-state')).toBe('unknown');
    }
  });

  it('a consulta falhada mostra o erro com nova tentativa, e nenhum 0', async () => {
    vi.mocked(getAgentInsights).mockRejectedValue(new ApiError(500, 'Erro no servidor'));
    renderTab();

    const erro = await screen.findByTestId('erro-da-consulta-do-agente');
    expect(erro).toHaveTextContent('Não foi possível ler as métricas deste período');
    expect(screen.getByRole('button', { name: 'Tentar de novo' })).toBeInTheDocument();
    expect(screen.getByTestId('kpi-agente-tasks-valor')).toHaveAttribute(
      'data-metric-state',
      'unknown',
    );
  });

  it('o 404 tem estado PRÓPRIO, sem número nenhum e SEM nova tentativa', async () => {
    vi.mocked(getAgentInsights).mockRejectedValue(new ApiError(404, 'Not Found'));
    const { container } = renderTab();

    const aviso = await screen.findByTestId('agente-nao-encontrado');
    expect(aviso).toHaveTextContent('As duas fontes discordam');
    // `404` é resposta, não falha de comunicação: repetir a pergunta não muda
    // a resposta.
    expect(screen.queryByRole('button', { name: 'Tentar de novo' })).not.toBeInTheDocument();
    expect(container.querySelectorAll('[data-metric-state]')).toHaveLength(0);
    expect(screen.queryByTestId('kpis-do-agente')).not.toBeInTheDocument();
  });

  it('o 404 é distinguível da consulta sem resposta', async () => {
    vi.mocked(getAgentInsights).mockRejectedValue(new ApiError(404, 'Not Found'));
    renderTab();

    await screen.findByTestId('agente-nao-encontrado');
    expect(screen.queryByTestId('erro-da-consulta-do-agente')).not.toBeInTheDocument();
  });

  it('o 200 ZERADO mostra os 0 como contagem feita, e NÃO o estado de recusa', async () => {
    // O par que torna os dois discriminantes. Um sem o outro não discrimina
    // nada: "sei que não existe" e "medi e não achei nada" são respostas
    // diferentes.
    vi.mocked(getAgentInsights).mockResolvedValue(agentInsightsFixture());
    renderTab();

    await aguardarResposta();
    const valor = screen.getByTestId('kpi-agente-tasks-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'zero');
    expect(valor).toHaveTextContent('0');
    expect(screen.queryByTestId('agente-nao-encontrado')).not.toBeInTheDocument();
    expect(screen.queryByTestId('erro-da-consulta-do-agente')).not.toBeInTheDocument();
  });

  it('a aba NÃO muda de comportamento por o agente estar inativo', async () => {
    // Inatividade é estado de CADASTRO, e a rota responde 200 do mesmo jeito.
    // A aba não recebe `isActive`, não o consulta e não o pode consultar — o
    // que ela apresenta é o que o agente executou enquanto esteve ativo.
    vi.mocked(getAgentInsights).mockResolvedValue(povoado);
    renderTab();

    await aguardarResposta();
    expect(screen.getByTestId('kpi-agente-tasks-valor')).toHaveTextContent('208');
    expect(screen.queryByTestId('agente-nao-encontrado')).not.toBeInTheDocument();
  });
});

describe('AgentInsightsTab — a composição', () => {
  beforeEach(() => {
    vi.mocked(getAgentInsights).mockReset();
    vi.mocked(getAgentInsights).mockResolvedValue(povoado);
  });

  it('o card de dias da semana NÃO traz nota de cenário', async () => {
    // A nota que dizia se o padrão semanal era próprio ou de quem aciona saiu
    // na conferência manual de 26/09, junto com a prop que a carregava.
    renderTab();

    await aguardarResposta();
    expect(screen.queryByTestId('dias-da-semana-nota')).not.toBeInTheDocument();
    expect(screen.getByTestId('card-dias-da-semana')).not.toHaveTextContent(
      /padrão semanal/i,
    );
  });

  it('a recusa de ENTRADA não tem elemento em lugar nenhum da aba', async () => {
    // Servida pela rota desde a #51 e sem elemento no artboard: entra na lista
    // de "servido e não desenhado", com gatilho, junto do embedding, da
    // profundidade e das tasks sem estado terminal (D10).
    vi.mocked(getAgentInsights).mockResolvedValue(
      agentInsightsFixture({
        errors: agentErrorsFixture({
          failedCount: 5,
          rejectedAtEntryCount: 9,
          byPhase: [{ phase: 'AgentRun', count: 5 }],
          rejectionsByReason: [{ reason: 'AgentInactive', count: 9 }],
        }),
      }),
    );
    const { container } = renderTab();

    await aguardarResposta();
    const texto = container.textContent ?? '';
    expect(texto).not.toContain('Recusadas antes de executar');
    expect(texto).not.toContain('Agente inativo');
  });

  it('caveat desconhecido aparece como aviso visível', async () => {
    vi.mocked(getAgentInsights).mockResolvedValue(
      agentInsightsFixture({
        delegation: agentDelegationFixture({ caveats: ['codigo-que-nao-existe'] }),
      }),
    );
    renderTab();

    expect(await screen.findByTestId('caveats-desconhecidos-do-agente')).toHaveTextContent(
      'codigo-que-nao-existe',
    );
  });

  it('caveat conhecido NÃO cai no aviso de desconhecidos', async () => {
    renderTab();

    await screen.findByTestId('kpis-do-agente');
    expect(screen.queryByTestId('caveats-desconhecidos-do-agente')).not.toBeInTheDocument();
  });

  it('sem catálogo, as linhas de delegação continuam com o identificador', async () => {
    renderTab({ semCatalogo: true });

    await aguardarResposta();
    expect(screen.getByTestId(`delega-para-${TARGET_ID}-nome`)).toHaveTextContent(TARGET_ID);
    expect(screen.getByTestId(`delega-para-${TARGET_ID}-valor`)).toHaveTextContent('30');
  });
});

describe('AgentInsightsTab — o guarda de acoplamento', () => {
  it('o módulo NÃO importa nada de features/agents', () => {
    // A seta entre as features é de mão única: `agents` importa `insights`,
    // nunca o contrário. O agente e o catálogo chegam por PROP.
    //
    // O guarda é estático porque é assim que ele pega o caso real: um import
    // de tipo some no build e passaria despercebido em qualquer teste de
    // comportamento, e é exatamente o que volta numa refatoração distraída.
    const fonte = readFileSync(
      'src/features/insights/components/AgentInsightsTab.tsx',
      'utf8',
    );

    expect(fonte).not.toMatch(/from\s+['"][^'"]*features\/agents/);
    expect(fonte).not.toMatch(/from\s+['"]\.\.\/\.\.\/agents/);
  });
});
