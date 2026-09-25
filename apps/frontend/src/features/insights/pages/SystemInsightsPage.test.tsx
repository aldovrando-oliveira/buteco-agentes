import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { SystemInsightsPage } from './SystemInsightsPage';
import { getSystemInsights } from '../api/insightsApi';
import { listAgents } from '../../agents/api/agentsApi';
import {
  systemInsightsFixture,
  tokensFixture,
  volumeFixture,
} from '../test/systemInsightsFixture';
import type { Agent } from '../../agents/types/agent';

vi.mock('../api/insightsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/insightsApi')>();
  return { ...actual, getSystemInsights: vi.fn() };
});

// MOCK PARCIAL: `importOriginal` preserva `request`/`ApiError`. Sem mockar
// `listAgents`, o catálogo escaparia para a rede.
vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

const ATENDENTE = '55555555-5555-5555-5555-555555555555';

const agente = {
  id: ATENDENTE,
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'anthropic',
  model: 'claude-opus-5',
  description: null,
  skills: [],
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  delegatesTo: [],
} as unknown as Agent;

const corpo = systemInsightsFixture({
  volume: volumeFixture({ executedTaskCount: 476, externalOriginTaskCount: 314 }),
  tokens: tokensFixture({
    conversation: { inputTokens: 9_600_000, outputTokens: 2_800_000, cachedInputTokens: null },
    byAgent: [{ agentId: ATENDENTE, inputTokens: 5_000_000, outputTokens: 1_200_000 }],
  }),
});

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <SystemInsightsPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('SystemInsightsPage', () => {
  beforeEach(() => {
    vi.mocked(getSystemInsights).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(getSystemInsights).mockResolvedValue(corpo);
    vi.mocked(listAgents).mockResolvedValue([agente]);
  });

  it('UMA requisição à rota agregada serve a página inteira', async () => {
    renderPage();

    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));
    expect(getSystemInsights).toHaveBeenCalledTimes(1);
  });

  it('trocar de período refaz a consulta com a janela nova', async () => {
    renderPage();
    await waitFor(() => expect(getSystemInsights).toHaveBeenCalledTimes(1));

    await userEvent.click(screen.getByRole('radio', { name: '7d' }));

    await waitFor(() => expect(getSystemInsights).toHaveBeenCalledTimes(2));
    const [from, to] = vi.mocked(getSystemInsights).mock.calls[1];
    const dias = (new Date(to).getTime() - new Date(from).getTime()) / (24 * 60 * 60 * 1000);
    expect(dias).toBe(7);
  });

  // ------------------------------------ O "MEDINDO DESDE" POR REGIME (spec)

  it('o cabeçalho traz a JANELA, e o "medindo desde" sai por REGIME', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));

    // A JANELA E O REGIME DE EXECUÇÃO no cabeçalho, lado a lado — é onde o
    // `Main.dc.html` os põe.
    expect(screen.getByTestId('janela-do-periodo')).toHaveTextContent(
      '15/09/2026 a 24/09/2026',
    );
    expect(screen.getByTestId('janela-do-periodo')).toHaveTextContent(
      'execução medida desde 22/09/2026',
    );

    // E os DOIS regimes, cada um junto do grupo que ele mede. Um texto único
    // mentiria sobre pelo menos um: o embedding começou um dia depois.
    //
    // O DE EXECUÇÃO, UMA VEZ — ele rege volume, série, desempenho e tokens de
    // conversa, e repeti-lo no cabeçalho de cinco cards seria a mesma data
    // cinco vezes. O DE EMBEDDING, no cabeçalho dos cards em que números dele
    // aparecem.
    const execucao = screen.getAllByTestId('medindo-desde-execution');
    expect(execucao).toHaveLength(1);
    // E ele vive DENTRO do cabeçalho, não numa linha solta abaixo da grade.
    expect(screen.getByTestId('janela-do-periodo')).toContainElement(execucao[0]);

    const embedding = screen.getAllByTestId('nota-de-regime');
    expect(embedding.length).toBeGreaterThan(0);
    expect(embedding[0]).toHaveTextContent('embedding medido desde 23/09/2026');
  });

  it('NEGATIVO: a nota de regime não se repete fora do cabeçalho do card', () => {
    // A primeira versão punha seis rodapés FLUTUANTES abaixo dos cards, com a
    // mesma data repetida três vezes cada. O protótipo não tem rodapé em card
    // nenhum, e o painel não tem esse idioma em lugar nenhum.
    //
    // A nota vive no cabeçalho do card, à direita, como o "máximo · mínimo" do
    // gráfico — e só onde o regime MUDA.
    renderPage();

    return waitFor(() => {
      const notas = screen.queryAllByTestId('nota-de-regime');
      const datas = notas.map((n) => n.textContent);
      // Nenhuma data se repete entre as notas de cabeçalho.
      expect(new Set(datas).size).toBe(datas.length);
      // E nenhuma delas repete o regime de execução, que já foi declarado.
      for (const texto of datas) {
        expect(texto).not.toContain('22/09/2026');
      }
    });
  });

  it('o "medindo desde" do cabeçalho NOMEIA o regime de que fala', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));

    // A spec proíbe um "medindo desde" único para a página porque, com dois
    // regimes de datas diferentes, um texto sem dono mentiria sobre um deles.
    // Este diz de qual fala — e o de embedding continua nos cabeçalhos dos
    // cards em que números dele aparecem.
    const cabecalho = screen.getByTestId('janela-do-periodo').textContent ?? '';
    expect(cabecalho).toContain('execução medida desde');
    expect(cabecalho).not.toMatch(/^medindo desde/);
    expect(cabecalho).not.toContain('23/09/2026');
  });

  it('NEGATIVO: não sobra nenhuma linha de regime SOLTA na página', async () => {
    // A última era abaixo da grade de KPIs, e o dono pediu para tirá-la. Toda
    // nota de regime vive agora ou no cabeçalho da página, ou no cabeçalho de
    // um card — nunca flutuando entre eles.
    renderPage();
    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));

    for (const nota of screen.getAllByTestId(/^medindo-desde-|^nota-de-regime$/)) {
      const dentroDoCabecalhoDaPagina = screen
        .getByTestId('janela-do-periodo')
        .contains(nota);
      const dentroDeCabecalhoDeCard = nota.closest('[data-testid^="card-"]') !== null;
      expect(dentroDoCabecalhoDaPagina || dentroDeCabecalhoDeCard).toBe(true);
    }
  });

  it('regime NOVO é absorvido sem mudança de estrutura', async () => {
    vi.mocked(getSystemInsights).mockResolvedValue(
      systemInsightsFixture({
        regimes: {
          execution: '2026-09-22T01:21:00-03:00',
          embedding: '2026-09-23T01:18:00-03:00',
          indexing: '2026-09-24T09:00:00-03:00',
        },
        volume: volumeFixture({ regime: 'indexing', executedTaskCount: 10 }),
      }),
    );
    renderPage();

    // O grupo que DECLARA o regime novo apresenta o início dele normalmente —
    // a tela não depende de quantos regimes existem nem de como se chamam.
    //
    // O NOME sai cru quando a tela não o conhece, pelo mesmo critério da fase
    // de falha desconhecida: mostrar o valor recebido é melhor que omitir ou
    // que reaproveitar o rótulo de outro.
    await waitFor(() =>
      expect(screen.getAllByTestId('medindo-desde-indexing')[0]).toHaveTextContent(
        'indexing medido desde 24/09/2026',
      ),
    );
  });

  // ---------------------------- AS DUAS CONSULTAS SÃO INDEPENDENTES (spec)

  it('o catálogo LENTO não segura os números', async () => {
    // Nunca resolve: a página não pode ficar esperando por ele.
    vi.mocked(listAgents).mockReturnValue(new Promise(() => {}));
    renderPage();

    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));
    expect(screen.getByTestId('kpi-tokens-valor')).toHaveTextContent('12,4 M');
    // Só a coluna de nome fica carregando.
    expect(screen.getByTestId(`agente-${ATENDENTE}-carregando`)).toBeInTheDocument();
    expect(screen.getByTestId(`agente-${ATENDENTE}-tokens`)).toHaveTextContent('6,2 M');
  });

  it('a falha do catálogo NÃO derruba a página', async () => {
    vi.mocked(listAgents).mockRejectedValue(new Error('catálogo fora do ar'));
    renderPage();

    await waitFor(() => expect(screen.getByTestId('catalogo-falhou')).toBeInTheDocument());

    // Os números continuam lá, e a nova tentativa é só do catálogo.
    expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476');
    expect(screen.getByTestId(`agente-${ATENDENTE}-tokens`)).toHaveTextContent('6,2 M');
    expect(screen.queryByTestId('erro-da-consulta')).toBeNull();
  });

  it('o nome do agente cruza com o catálogo quando ele responde', async () => {
    renderPage();

    await waitFor(() =>
      expect(screen.getByTestId(`agente-${ATENDENTE}-nome`)).toHaveTextContent('Atendente'),
    );
  });

  // ------------------------ A FALHA DA ROTA AGREGADA: TRAVESSÃO, NUNCA ZERO

  it('a falha da rota agregada apresenta travessão com razão e nova tentativa', async () => {
    vi.mocked(getSystemInsights).mockRejectedValue(new Error('Tempo de resposta esgotado.'));
    renderPage();

    await waitFor(() => expect(screen.getByTestId('erro-da-consulta')).toBeInTheDocument());

    // A razão ao lado, e a ação de nova tentativa.
    expect(screen.getByTestId('erro-da-consulta')).toHaveTextContent('Tempo de resposta esgotado.');
    expect(screen.getByRole('button', { name: 'Tentar de novo' })).toBeInTheDocument();

    // Travessão no lugar dos números.
    expect(screen.getByTestId('kpi-tasks-valor')).toHaveAttribute('data-metric-state', 'unknown');
  });

  it('NEGATIVO: com a rota falhando, NENHUM 0 aparece nos números da página', async () => {
    vi.mocked(getSystemInsights).mockRejectedValue(new Error('Tempo de resposta esgotado.'));
    renderPage();

    await waitFor(() => expect(screen.getByTestId('erro-da-consulta')).toBeInTheDocument());

    // O guarda que fecha a página: requisição que não respondeu não é
    // evidência de ausência, e o esqueleto que sustenta a tela tem contagens
    // em 0 por serem `number` — é `queryState` que vence sobre elas.
    const valores = document.querySelectorAll('[data-metric-state]');
    expect(valores.length).toBeGreaterThan(0);
    for (const valor of valores) {
      expect(valor.getAttribute('data-metric-state')).not.toBe('zero');
      expect(valor.textContent).not.toContain('0');
    }
  });

  // ------------------------- O ESTADO PENDENTE (convenções 13, 14 e 15)

  it('NEGATIVO: ENQUANTO A CONSULTA CORRE, nenhum 0 e nenhuma frase de ausência', async () => {
    // O GUARDA QUE FALTAVA, e ele só existe porque falhou contra o defeito
    // real (convenção 15).
    //
    // Pego na conferência manual de 24/09, com a API derrubada: a página exibia
    // "Tasks executadas: 0", "Chamadas ao provedor: 0" e "Nenhum consumo por
    // provedor neste período" ENQUANTO a consulta ainda corria. A causa era
    // `isError ? 'failed' : 'ok'` — a negação deixa o pendente cair em `'ok'`,
    // e o esqueleto que sustenta a tela tem contagens `int` em 0.
    //
    // É o modo de falha que o quadro 3 do `Estados.dc.html` proíbe por escrito:
    // requisição que não respondeu não é evidência de ausência. Nenhum teste
    // pegou porque todos cobriam `isError`, e três estados foram modelados como
    // dois.
    vi.mocked(getSystemInsights).mockReturnValue(new Promise(() => {}));
    renderPage();

    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toBeInTheDocument());

    // Travessão em todo valor, e NENHUM zero medido.
    const valores = document.querySelectorAll('[data-metric-state]');
    expect(valores.length).toBeGreaterThan(0);
    for (const valor of valores) {
      expect(valor.getAttribute('data-metric-state')).toBe('unknown');
      expect(valor.textContent).not.toContain('0');
    }

    // E nenhuma frase que afirme ausência — elas pertencem ao zero MEDIDO.
    const texto = screen.getByTestId('pagina-insights').textContent ?? '';
    expect(texto).not.toMatch(/Nenhum consumo por provedor/);
    expect(texto).not.toMatch(/Nenhuma chamada a modelo/);
    expect(texto).not.toMatch(/Nenhum consumo por agente/);
    expect(texto).not.toMatch(/Nenhuma falha nem recusa/);
  });

  it('o pendente diz CONSULTANDO, e não que a consulta falhou', async () => {
    // Os dois estados produzem o mesmo travessão e continuam separados na
    // razão: "consultando" e "não respondeu" não são a mesma frase para quem lê,
    // e o segundo convidaria a uma nova tentativa que não é necessária.
    vi.mocked(getSystemInsights).mockReturnValue(new Promise(() => {}));
    renderPage();

    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toBeInTheDocument());

    expect(screen.getByTestId('kpi-tasks-valor')).toHaveAttribute(
      'title',
      'Consultando as métricas deste período.',
    );
    // E o alerta de erro NÃO aparece enquanto ela corre.
    expect(screen.queryByTestId('erro-da-consulta')).toBeNull();
  });

  it('o banner de não-terminais não aparece enquanto a consulta corre', async () => {
    // O esqueleto tem as duas contagens em 0, então ele não renderiza — mas se
    // um dia tiver outro default, o aviso não pode nascer de dado que não veio.
    vi.mocked(getSystemInsights).mockReturnValue(new Promise(() => {}));
    renderPage();

    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toBeInTheDocument());
    expect(screen.queryByTestId('banner-nao-terminais')).toBeNull();
  });

  it('a nova tentativa refaz a consulta', async () => {
    vi.mocked(getSystemInsights).mockRejectedValueOnce(new Error('falhou'));
    renderPage();
    await waitFor(() => expect(screen.getByTestId('erro-da-consulta')).toBeInTheDocument());

    vi.mocked(getSystemInsights).mockResolvedValue(corpo);
    await userEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }));

    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));
  });

  // ---------------------------------------------- CÓDIGO DE CAVEAT NOVO

  it('código de parcialidade DESCONHECIDO aparece cru, e não some', async () => {
    vi.mocked(getSystemInsights).mockResolvedValue(
      systemInsightsFixture({
        errors: { ...corpo.errors, caveats: [...corpo.errors.caveats, 'limite-novo-do-servidor'] },
      }),
    );
    renderPage();

    await waitFor(() =>
      expect(screen.getByTestId('caveats-desconhecidos')).toHaveTextContent(
        'limite-novo-do-servidor',
      ),
    );
  });

  it('sem código desconhecido, nenhum aviso aparece', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));

    expect(screen.queryByTestId('caveats-desconhecidos')).toBeNull();
  });

  it('NEGATIVO: o código sem número nesta página não é renderizado solto', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));

    // `residual-is-not-only-tools` qualifica o resíduo, que o protótipo não
    // desenha. Um texto de limitação sem o número que ele limita não tem o que
    // qualificar — e não pode aparecer nem como código cru, porque a tela o
    // CONHECE.
    const texto = screen.getByTestId('pagina-insights').textContent ?? '';
    expect(texto).not.toContain('residual-is-not-only-tools');
    expect(texto).not.toMatch(/espera de lock/i);
  });

  it('o aviso de não-terminais FECHA a página, e não a abre', async () => {
    // O guarda de POSIÇÃO, que faltava — e é o que teria pego o aviso no topo.
    //
    // O `Main.dc.html` põe o aviso depois do card de Motivos, no rodapé. Foi
    // implementado no topo por julgamento do agente de que aviso vai em cima;
    // o protótipo diz o contrário, e tem razão: o que ele relata são algumas
    // tasks entre as do período, não uma interrupção de serviço. No topo,
    // empurrava os números para baixo e tomava a primeira leitura da tela.
    //
    // Nenhum caso anterior olhava ORDEM. Todos afirmavam presença — que é a
    // propriedade que o jsdom enxerga melhor, e a que nunca esteve errada.
    vi.mocked(getSystemInsights).mockResolvedValue(
      systemInsightsFixture({
        volume: volumeFixture({ executedTaskCount: 476 }),
        errors: {
          ...corpo.errors,
          nonTerminal: {
            openExecutionCount: 3,
            neverConsumedCount: 5,
            observedStates: ['Submitted', 'Working'],
          },
        },
      }),
    );
    renderPage();

    await waitFor(() => expect(screen.getByTestId('banner-nao-terminais')).toBeInTheDocument());

    const banner = screen.getByTestId('banner-nao-terminais');
    const kpis = screen.getByTestId('kpis');
    const motivos = screen.getByTestId('card-motivos');

    // `DOCUMENT_POSITION_PRECEDING` = o outro nó vem ANTES do banner.
    const vemAntes = (el: Element) =>
      Boolean(
        banner.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_PRECEDING,
      );

    expect(vemAntes(kpis)).toBe(true);
    expect(vemAntes(motivos)).toBe(true);
  });

  it('o mapa de calor usa o fuso DA RESPOSTA', async () => {
    renderPage();

    await waitFor(() =>
      expect(screen.getByTestId('card-mapa-de-calor')).toHaveTextContent(
        'horário local (America/Sao_Paulo)',
      ),
    );
  });
});
