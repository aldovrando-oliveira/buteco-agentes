import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { SystemInsightsPage } from './SystemInsightsPage';
import { getSystemInsights } from '../api/insightsApi';
import { listAgents } from '../../agents/api/agentsApi';
import {
  REGIMES_FIXTURE,
  errorsFixture,
  systemInsightsFixture,
  tokensFixture,
  volumeFixture,
} from '../test/systemInsightsFixture';
import type { Agent } from '../../agents/types/agent';
import { DEFAULT_INSIGHTS_PERIOD } from '../utils/insightsWindow';

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

/**
 * `createMemoryRouter` E NÃO `MemoryRouter`, e a troca é pelo que ela devolve.
 *
 * A página passou a LER E ESCREVER o período no endereço (#85), então os guardas
 * precisam afirmar o endereço — inclusive que ele NÃO foi reescrito, que é uma
 * afirmação sobre o que não aconteceu. `MemoryRouter` não expõe a localização;
 * o router devolvido por `createMemoryRouter` expõe `state.location.search`.
 *
 * É a mesma forma que `AgentDetailPage.test.tsx` já usa, pela mesma razão.
 *
 * A rota de `/agents/:id` existe porque o card de consumo por agente renderiza
 * `<Link>` para lá, e um `Link` sem rota casada avisa no console.
 */
function renderPage(search = '') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/insights', element: <SystemInsightsPage /> },
      { path: '/agents/:id', element: <p>detalhe do agente</p> },
    ],
    { initialEntries: [`/insights${search}`] },
  );

  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );

  return router;
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

  // --------------------------------- O PERÍODO NO ENDEREÇO (#85)
  //
  // O que a #85 entrega é o período VIAJANDO, e a travessia inteira tem guarda
  // próprio em `src/app/router.test.tsx`, que NAVEGA. Os quatro casos abaixo são a
  // metade de origem: o endereço decide a abertura, a troca escreve o endereço, e
  // os dois caminhos de ausência caem no padrão sem reescrever.

  it('o endereço decide o período de abertura, e a consulta usa aquela janela', async () => {
    const router = renderPage('?period=90d');

    await waitFor(() => expect(getSystemInsights).toHaveBeenCalledTimes(1));

    // A LARGURA, não os instantes (D8): a diferença entre os dois limites não
    // depende de quando o teste roda, então não há relógio a controlar. É a mesma
    // forma do caso "trocar de período refaz a consulta" acima.
    const [from, to] = vi.mocked(getSystemInsights).mock.calls[0];
    const dias = (new Date(to).getTime() - new Date(from).getTime()) / (24 * 60 * 60 * 1000);
    expect(dias).toBe(90);

    // O seletor marcado, e não só a consulta: é o que o operador lê.
    expect(screen.getByRole('radio', { name: '90d' })).toBeChecked();
    // E recarregar o mesmo endereço reabre o mesmo período, que é o endereço
    // continuar sendo o que ele é — nada o normalizou no caminho.
    expect(router.state.location.search).toBe('?period=90d');
  });

  it('trocar de período escreve o endereço', async () => {
    // Sem isto o período não sobrevive a refresh nem viaja em link colado, que é
    // metade do que a issue pede. A outra metade é o link do ranking.
    const router = renderPage();
    await waitFor(() => expect(getSystemInsights).toHaveBeenCalledTimes(1));

    await userEvent.click(screen.getByRole('radio', { name: '7d' }));

    await waitFor(() =>
      expect(new URLSearchParams(router.state.location.search).get('period')).toBe('7d'),
    );
  });

  it('endereço SEM período abre no padrão, e o endereço NÃO é reescrito', async () => {
    // O par positivo, e é o caminho de TODO link já compartilhado: os endereços
    // que existiam antes desta change não têm período, e continuam valendo com o
    // mesmo significado que tinham.
    //
    // A precondição afirmada é o estado observável MAIS PRÓXIMO do elemento
    // negado: o endereço depois de a página ter respondido e antes de qualquer
    // interação. Afirmá-lo depois de um clique mediria um endereço que a própria
    // interação já teria escrito.
    const router = renderPage();

    await waitFor(() => expect(getSystemInsights).toHaveBeenCalledTimes(1));

    expect(screen.getByRole('radio', { name: DEFAULT_INSIGHTS_PERIOD })).toBeChecked();
    expect(router.state.location.search).toBe('');
  });

  it('período NÃO RECONHECIDO abre no padrão sem quebrar, e o endereço NÃO é reescrito', async () => {
    // Mesmo contrato que `parseTab` declara para a aba, e por simetria medida: a
    // página de detalhe já trata identificação de aba desconhecida assim, com
    // cenário de spec próprio. Duas chaves do mesmo endereço com disciplinas
    // opostas seriam duas regras para o operador aprender.
    //
    // E o silêncio não esconde nada: a janela consultada vai para o cabeçalho
    // ecoada pela RESPOSTA, então o operador vê em qual período está.
    const router = renderPage('?period=180d');

    // Esperar a RESPOSTA, não a chamada: o KPI existe desde o primeiro render,
    // sustentado pelo esqueleto, e afirmar o valor logo após a chamada mede o
    // estado pendente — que é travessão, não número. A primeira escrita deste
    // guarda reprovou assim.
    await waitFor(() =>
      expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'),
    );

    expect(screen.getByRole('radio', { name: DEFAULT_INSIGHTS_PERIOD })).toBeChecked();
    expect(router.state.location.search).toBe('?period=180d');
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

    // A EXPRESSÃO FOI ALARGADA DE `^nota-de-regime$` PARA `^nota-de-regime`: a
    // nota do regime de recusa chega com sufixo (`nota-de-regime-rejection`), e a
    // âncora de fim a deixava de fora da varredura. Guarda que não varre o
    // elemento novo dá impressão de cobertura sem ter.
    for (const nota of screen.getAllByTestId(/^medindo-desde-|^nota-de-regime/)) {
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

  it('regime CONHECIDO não sai cru — a recusa aparece com rótulo de operador', async () => {
    // O DISCRIMINANTE DESTA CHANGE, e ele não é o caso de cima.
    //
    // O caso `regime NOVO é absorvido` passa um nome que a tela GENUINAMENTE não
    // conhece, e para ele o cru é o certo. `rejection` é o oposto: regime que a
    // tela conhece — a rota o exige em `MetricsOptions.All`, o contrato o declara
    // em campo próprio, e esta página o apresenta — caindo no caminho do
    // desconhecido. **Nenhum arranjo daquele caso separa os dois**, porque ele
    // nunca passa um conhecido-sem-rótulo.
    //
    // Sem este guarda, a tela escreveria "rejection medido desde 26/09/2026", em
    // inglês, em português.
    renderPage();
    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));

    const nota = screen.getByTestId('nota-de-regime-rejection');
    expect(nota).toHaveTextContent('recusa medida desde 26/09/2026');
    expect(nota.textContent).not.toContain('rejection');
  });

  it('o card de Motivos NÃO recebe nota de regime, e o de provedor CONTINUA recebendo', async () => {
    // O PAR, e é ele que prova a correção do achado da conferência do dono.
    //
    // O defeito morava na fiação: a página passava `regimeNote` ao card de
    // Motivos, que mostra TRÊS populações de três regimes — fase de execução,
    // falha de indexação e motivo de recusa. Um nome de regime no cabeçalho
    // afirma que tudo ali é daquele, quando dois terços não são.
    //
    // O NEGATIVO SOZINHO NÃO BASTARIA: ele passaria se a página parasse de passar
    // a nota a TODOS os cards, que é a regressão fácil de cometer ao mexer num
    // argumento compartilhado. Por isso o positivo vem no mesmo caso.
    // A PRECONDIÇÃO É A QUE PRODUZ A NOTA, E A PRIMEIRA VERSÃO ERROU ISSO.
    //
    // Ela afirmava só que `REGIMES_FIXTURE` tem `embedding` — e passou verde
    // contra o defeito, porque a fixture padrão não traz `byPhase` nem
    // `indexingFailures`: sem linha de falha não há grupo, e sem grupo não havia
    // nota a negar. **Terceira vez nesta change que um guarda de ausência passa
    // por falta do estado que ele nega.** A precondição certa é a linha de falha,
    // não o regime no mapa.
    vi.mocked(getSystemInsights).mockResolvedValue({
      ...corpo,
      errors: errorsFixture({
        byPhase: [{ phase: 'AgentRun', count: 6 }],
        indexingFailures: [{ outcome: 'Failed', failurePhase: 'Chunking', count: 2 }],
        rejectedAtEntryCount: 5,
        rejectionsByReason: [{ reason: 'AgentInactive', count: 5 }],
      }),
    });
    renderPage();
    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));

    // Precondição: o grupo que CARREGAVA a nota está na tela, e o regime dele
    // está na resposta — é exatamente o estado em que a nota apareceria.
    expect(screen.getByTestId('grupo-motivos-de-falha')).toBeInTheDocument();
    expect(Object.keys(REGIMES_FIXTURE)).toContain('embedding');

    // O positivo: o card de consumo por provedor continua declarando o dele.
    const provedor = screen.getByTestId('card-consumo-por-provedor');
    expect(provedor.querySelector('[data-testid="nota-de-regime"]')).not.toBeNull();

    // O negativo: o de Motivos não declara nenhum.
    const motivos = screen.getByTestId('card-motivos');
    expect(motivos.querySelector('[data-testid="nota-de-regime"]')).toBeNull();
    expect(motivos.textContent ?? '').not.toContain('medido desde');
  });

  it('NEGATIVO: nenhum nome de regime do FIO aparece na página', async () => {
    // A varredura, e não só o caso do rótulo: os três nomes de
    // `MetricsOptions.All` são chaves de contrato, nunca texto de operador. Um
    // quarto sítio de tradução esquecido reprova aqui, e não no caso de cima.
    renderPage();
    await waitFor(() => expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476'));

    const pagina = screen.getByTestId('pagina-insights').textContent ?? '';
    for (const doFio of ['execution', 'rejection']) {
      expect(pagina).not.toContain(doFio);
    }
    // `embedding` é a exceção declarada: é o nome que o dono aprovou na tela,
    // porque é o termo que quem opera usa. Ele aparece, e é de propósito.
    expect(pagina).toContain('embedding medido desde');
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

  it('a coluna Falhas da tabela está LIGADA a `errors.byAgent`, e a #84 fecha pela fiação', async () => {
    // A RÉGUA DA FIAÇÃO, e ela achou buraco de verdade nesta change.
    //
    // Os guardas da #84 e da #86 vivem em `AgentConsumptionCard.test.tsx` e provam
    // o COMPONENTE. Trocando `failuresByAgent={insights.errors.byAgent}` por `{[]}`
    // aqui na página — que esvaziaria a coluna Falhas inteira no app real e faria a
    // linha da #84 desaparecer de novo — os 457 testes de `insights` passavam
    // TODOS. O defeito morava na fiação, e nenhum guarda o via.
    //
    // Este guarda afirma a ligação pelo caso mais forte que ela sustenta: um
    // agente presente SÓ em `errors.byAgent`, ausente de `tokens.byAgent`,
    // chegando à tela pela página de verdade.
    const SEM_CONSUMO = '77777777-7777-7777-7777-777777777777';
    vi.mocked(getSystemInsights).mockResolvedValue({
      ...corpo,
      errors: errorsFixture({
        failedCount: 2,
        byAgent: [{ agentId: SEM_CONSUMO, provider: null, model: null, failedCount: 2 }],
      }),
    });
    renderPage();

    await waitFor(() =>
      expect(screen.getByTestId(`agente-${SEM_CONSUMO}-falhas`)).toHaveTextContent('2'),
    );

    // E o consumo dele fica VAZIO, não em zero: ele não chamou o provedor.
    expect(screen.getByTestId(`agente-${SEM_CONSUMO}-tokens`)).toHaveAttribute(
      'data-metric-state',
      'empty',
    );
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
