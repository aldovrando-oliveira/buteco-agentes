import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { InventoryPage } from './InventoryPage';
import { listAgents } from '../../agents/api/agentsApi';
import { listMcpServers } from '../../mcp-servers/api/mcpServersApi';
import { listKnowledgeBases } from '../../knowledge-bases/api/knowledgeBasesApi';
import { listChannels } from '../../channels/api/channelsApi';
import {
  getMessagesSummary,
  getSessionsSummary,
  listChannelSessions,
} from '../../sessions/api/sessionsApi';
import type { Agent } from '../../agents/types/agent';
import type { McpServer } from '../../mcp-servers/types/mcpServer';
import type { KnowledgeBase } from '../../knowledge-bases/types/knowledgeBase';
import type { Channel } from '../../channels/types/channel';

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

vi.mock('../../mcp-servers/api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../mcp-servers/api/mcpServersApi')>();
  return { ...actual, listMcpServers: vi.fn() };
});

vi.mock('../../knowledge-bases/api/knowledgeBasesApi', async (importOriginal) => {
  const actual =
    await importOriginal<typeof import('../../knowledge-bases/api/knowledgeBasesApi')>();
  return { ...actual, listKnowledgeBases: vi.fn() };
});

vi.mock('../../channels/api/channelsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../channels/api/channelsApi')>();
  return { ...actual, listChannels: vi.fn() };
});

// MOCK PARCIAL: `importOriginal` preserva `request`/`ApiError`. As duas funções
// de resumo precisam estar aqui, ou os itens de atividade escapam para a rede.
// `listChannelSessions` também é mockada — não porque a tela a chame, mas para
// o teste poder afirmar que ela NÃO é chamada (apuração única, design.md, D2).
vi.mock('../../sessions/api/sessionsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../sessions/api/sessionsApi')>();
  return {
    ...actual,
    getSessionsSummary: vi.fn(),
    getMessagesSummary: vi.fn(),
    listChannelSessions: vi.fn(),
  };
});

const agent: Agent = {
  id: '55555555-5555-5555-5555-555555555555',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  description: null,
  skills: [],
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  delegatesTo: [],
  knowledgeBases: [],
  a2a: null,
};

const mcpServer: McpServer = {
  id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  name: 'Zendesk MCP',
  description: 'Servidor MCP do Zendesk',
  url: 'https://mcp.zendesk.example/sse',
  authType: 'BearerToken',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

const knowledgeBase: KnowledgeBase = {
  id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
  name: 'Políticas',
  description: 'Políticas de atendimento',
  isActive: true,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
};

const channel: Channel = {
  id: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
  name: 'WhatsApp principal',
  channelType: 'waha',
  agentId: agent.id,
  webhookUrl: 'https://inbox.example/webhooks/waha',
  isActive: true,
  createdAt: '2026-08-10T00:00:00Z',
  updatedAt: '2026-08-10T00:00:00Z',
};

function comIds<T extends { id: string }>(modelo: T, quantidade: number): T[] {
  return Array.from({ length: quantidade }, (_, indice) => ({
    ...modelo,
    id: `${indice}`.padStart(8, '0') + modelo.id.slice(8),
  }));
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <InventoryPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

// Cada item é procurado pelo seu data-testid, e nunca pelo texto: o que a tela
// afirma sobre um catálogo tem de ser lido DENTRO do item daquele catálogo, ou a
// asserção de independência entre eles não prova nada.
const item = (chave: string) => screen.getByTestId(`inventario-${chave}`);

describe('InventoryPage', () => {
  beforeEach(() => {
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
    vi.mocked(listMcpServers).mockReset();
    vi.mocked(listMcpServers).mockResolvedValue([]);
    vi.mocked(listKnowledgeBases).mockReset();
    vi.mocked(listKnowledgeBases).mockResolvedValue([]);
    vi.mocked(listChannels).mockReset();
    vi.mocked(listChannels).mockResolvedValue([]);
    vi.mocked(getSessionsSummary).mockReset();
    vi.mocked(getSessionsSummary).mockResolvedValue({ startedCount: 0 });
    vi.mocked(getMessagesSummary).mockReset();
    vi.mocked(getMessagesSummary).mockResolvedValue({ inboundCount: 0 });
    vi.mocked(listChannelSessions).mockReset();
  });

  it('exibe a contagem de cada catálogo, uma vez, com o atalho para a listagem', async () => {
    vi.mocked(listAgents).mockResolvedValue(comIds(agent, 3));
    vi.mocked(listMcpServers).mockResolvedValue(comIds(mcpServer, 2));
    vi.mocked(listKnowledgeBases).mockResolvedValue(comIds(knowledgeBase, 9));
    vi.mocked(listChannels).mockResolvedValue(comIds(channel, 1));

    renderPage();

    await waitFor(() =>
      expect(within(item('agents')).getByTestId('inventario-agents-contagem')).toHaveTextContent(
        '3',
      ),
    );
    expect(
      within(item('mcp-servers')).getByTestId('inventario-mcp-servers-contagem'),
    ).toHaveTextContent('2');
    expect(
      within(item('knowledge-bases')).getByTestId('inventario-knowledge-bases-contagem'),
    ).toHaveTextContent('9');
    expect(within(item('channels')).getByTestId('inventario-channels-contagem')).toHaveTextContent(
      '1',
    );

    expect(within(item('agents')).getByRole('link', { name: 'Ver agentes' })).toHaveAttribute(
      'href',
      '/agents',
    );
    expect(
      within(item('mcp-servers')).getByRole('link', { name: 'Ver servidores MCP' }),
    ).toHaveAttribute('href', '/mcp-servers');
    expect(
      within(item('knowledge-bases')).getByRole('link', { name: 'Ver bases' }),
    ).toHaveAttribute('href', '/knowledge-bases');
    expect(within(item('channels')).getByRole('link', { name: 'Ver canais' })).toHaveAttribute(
      'href',
      '/channels',
    );
  });

  it('usa o singular quando o catálogo tem um único registro', async () => {
    vi.mocked(listAgents).mockResolvedValue(comIds(agent, 1));

    renderPage();

    await waitFor(() => expect(within(item('agents')).getByText('agente')).toBeInTheDocument());
    expect(within(item('agents')).queryByText('agentes')).not.toBeInTheDocument();
  });

  it('catálogo vazio afirma a ausência, e não exibe o símbolo de desconhecido', async () => {
    vi.mocked(listAgents).mockResolvedValue([]);

    renderPage();

    // Zero MEDIDO: a consulta respondeu e não há registro. Dizer "Nenhum agente"
    // é verdade; dizer "—" gastaria o vocabulário de "não sei" num fato sabido.
    await waitFor(() =>
      expect(within(item('agents')).getByText('Nenhum agente')).toBeInTheDocument(),
    );
    expect(within(item('agents')).queryByText('—')).not.toBeInTheDocument();
  });

  it('catálogo vazio não exibe a contagem zero como número', async () => {
    vi.mocked(listAgents).mockResolvedValue([]);

    renderPage();

    await waitFor(() =>
      expect(within(item('agents')).getByText('Nenhum agente')).toBeInTheDocument(),
    );
    // A NEGATIVA que impede o "0" de voltar: o elemento de contagem não existe
    // quando não há o que contar, então nenhum "0" pode ser renderizado ali.
    expect(
      within(item('agents')).queryByTestId('inventario-agents-contagem'),
    ).not.toBeInTheDocument();
  });

  it('consulta que não respondeu não vira zero nem ausência, e diz a razão', async () => {
    vi.mocked(listAgents).mockRejectedValue(new Error('rede caiu'));

    renderPage();

    await waitFor(() => expect(within(item('agents')).getByText('—')).toBeInTheDocument());
    expect(
      within(item('agents')).getByText('Não foi possível consultar os agentes.'),
    ).toBeInTheDocument();
    expect(within(item('agents')).queryByText('Nenhum agente')).not.toBeInTheDocument();
    expect(
      within(item('agents')).queryByTestId('inventario-agents-contagem'),
    ).not.toBeInTheDocument();
  });

  it('consulta em andamento não afirma contagem nem indisponibilidade', () => {
    vi.mocked(listAgents).mockReturnValue(new Promise(() => {}));

    renderPage();

    expect(within(item('agents')).getByText('Consultando…')).toBeInTheDocument();
    expect(within(item('agents')).queryByText('—')).not.toBeInTheDocument();
    expect(within(item('agents')).queryByText('Nenhum agente')).not.toBeInTheDocument();
    expect(
      within(item('agents')).queryByTestId('inventario-agents-contagem'),
    ).not.toBeInTheDocument();
  });

  it('a falha de um catálogo não apaga os outros três', async () => {
    // O catálogo que falha é o de canais de propósito: ele é o único que vem de
    // apps/inbox, e é a falha que o risco declarado no design.md antecipa.
    vi.mocked(listChannels).mockRejectedValue(new Error('apps/inbox fora do ar'));
    vi.mocked(listAgents).mockResolvedValue(comIds(agent, 3));
    vi.mocked(listMcpServers).mockResolvedValue(comIds(mcpServer, 2));
    vi.mocked(listKnowledgeBases).mockResolvedValue(comIds(knowledgeBase, 9));

    renderPage();

    await waitFor(() => expect(within(item('channels')).getByText('—')).toBeInTheDocument());
    expect(within(item('agents')).getByTestId('inventario-agents-contagem')).toHaveTextContent('3');
    expect(
      within(item('mcp-servers')).getByTestId('inventario-mcp-servers-contagem'),
    ).toHaveTextContent('2');
    expect(
      within(item('knowledge-bases')).getByTestId('inventario-knowledge-bases-contagem'),
    ).toHaveTextContent('9');
  });

  it('a consulta lenta de um catálogo não segura os que já responderam', async () => {
    vi.mocked(listChannels).mockReturnValue(new Promise(() => {}));
    vi.mocked(listAgents).mockResolvedValue(comIds(agent, 3));

    renderPage();

    // A NEGATIVA contra o portão de carregamento combinado de
    // ChannelListPage.tsx:14: os agentes aparecem enquanto os canais ainda
    // consultam.
    await waitFor(() =>
      expect(within(item('agents')).getByTestId('inventario-agents-contagem')).toHaveTextContent(
        '3',
      ),
    );
    expect(within(item('channels')).getByText('Consultando…')).toBeInTheDocument();
  });

  it('a nova tentativa refaz só a consulta daquele catálogo', async () => {
    const user = userEvent.setup();
    vi.mocked(listChannels).mockRejectedValue(new Error('apps/inbox fora do ar'));
    vi.mocked(listAgents).mockResolvedValue(comIds(agent, 3));

    renderPage();

    await waitFor(() => expect(within(item('channels')).getByText('—')).toBeInTheDocument());

    const chamadasDeAgentes = vi.mocked(listAgents).mock.calls.length;
    const chamadasDeCanais = vi.mocked(listChannels).mock.calls.length;

    await user.click(within(item('channels')).getByRole('button', { name: 'Tentar novamente' }));

    // Só a consulta daquele catálogo é refeita. Se a implementação chamasse
    // invalidateQueries() sem chave, as quatro seriam refeitas e esta asserção
    // reprovaria (design.md, D5).
    await waitFor(() =>
      expect(vi.mocked(listChannels).mock.calls.length).toBeGreaterThan(chamadasDeCanais),
    );
    expect(vi.mocked(listAgents).mock.calls.length).toBe(chamadasDeAgentes);
  });

  it('a nova tentativa em andamento troca o desconhecido por consulta em andamento', async () => {
    const user = userEvent.setup();
    vi.mocked(listChannels).mockRejectedValue(new Error('apps/inbox fora do ar'));

    renderPage();

    await waitFor(() => expect(within(item('channels')).getByText('—')).toBeInTheDocument());

    vi.mocked(listChannels).mockReturnValue(new Promise(() => {}));
    await user.click(within(item('channels')).getByRole('button', { name: 'Tentar novamente' }));

    await waitFor(() =>
      expect(within(item('channels')).getByText('Consultando…')).toBeInTheDocument(),
    );
    expect(within(item('channels')).queryByText('—')).not.toBeInTheDocument();
  });

  it('nenhum item reproduz o texto explicativo da listagem do seu catálogo', async () => {
    vi.mocked(listAgents).mockResolvedValue(comIds(agent, 3));
    vi.mocked(listMcpServers).mockResolvedValue(comIds(mcpServer, 2));
    vi.mocked(listKnowledgeBases).mockResolvedValue(comIds(knowledgeBase, 9));
    vi.mocked(listChannels).mockResolvedValue(comIds(channel, 1));

    renderPage();

    await waitFor(() =>
      expect(within(item('agents')).getByTestId('inventario-agents-contagem')).toHaveTextContent(
        '3',
      ),
    );

    // NEGATIVA ESTRUTURAL — ela enumera o que o item TEM, em vez de listar as
    // frases que ele não pode ter.
    //
    // A forma que parece óbvia é `not.toMatch(/cada um atende.../)` com o teor
    // de cada subtítulo. Ela está PROIBIDA pelo requisito, e o motivo é que
    // copiar aqui o texto de uma listagem faria deste teste uma SEGUNDA fonte de
    // verdade daquele texto: mudariam o subtítulo lá e este teste seguiria verde
    // contra a versão antiga, protegendo uma frase que já não existe.
    //
    // Afirmar o conteúdo INTEIRO, por igualdade, não conhece nenhum texto de
    // outra tela e ainda assim pega o modo de falha real — alguém acrescentar
    // prosa "para ficar igual à lista" quebra a igualdade, seja qual for a
    // frase. O textContent concatena sem separador: rótulo, número, unidade e
    // atalho, nada entre eles.
    expect(item('agents').textContent).toBe('Agentes3agentesVer agentes');
    expect(item('mcp-servers').textContent).toBe('Servidores MCP2servidoresVer servidores MCP');
    expect(item('knowledge-bases').textContent).toBe('Bases de conhecimento9basesVer bases');
    expect(item('channels').textContent).toBe('Canais1canalVer canais');
  });

  // ------------------------------------------------------------------------
  // ITENS DE ATIVIDADE (frontend-inventario-atividade-periodo)
  // ------------------------------------------------------------------------

  const atividades = [
    {
      chave: 'sessions',
      mock: () => vi.mocked(getSessionsSummary),
      corpo: (n: number) => ({ startedCount: n }),
      singular: 'sessão',
      plural: 'sessões',
      zero: 'Nenhuma sessão iniciada',
      razao: 'Não foi possível consultar as sessões iniciadas.',
      rotulo: 'Sessões iniciadas',
    },
    {
      chave: 'messages',
      mock: () => vi.mocked(getMessagesSummary),
      corpo: (n: number) => ({ inboundCount: n }),
      singular: 'mensagem',
      plural: 'mensagens',
      zero: 'Nenhuma mensagem recebida',
      razao: 'Não foi possível consultar as mensagens recebidas.',
      rotulo: 'Mensagens recebidas',
    },
  ] as const;

  // O mock de cada cliente é tipado por ele; aqui só importa o que ele devolve.
  const responde = (a: (typeof atividades)[number], valor: Promise<unknown>) =>
    (a.mock() as unknown as ReturnType<typeof vi.fn>).mockReturnValue(valor);

  describe.each(atividades)('item de atividade $chave', (a) => {
    const contagem = () => within(item(a.chave)).queryByTestId(`inventario-${a.chave}-contagem`);
    const janela = () => within(item(a.chave)).getByTestId(`inventario-${a.chave}-janela`);

    it('apresenta a contagem da rota, no plural, com a janela', async () => {
      responde(a, Promise.resolve(a.corpo(42)));

      renderPage();

      await waitFor(() => expect(contagem()).toHaveTextContent('42'));
      expect(within(item(a.chave)).getByText(a.plural)).toBeInTheDocument();
      expect(janela()).toHaveTextContent('últimos 7 dias');
    });

    it('usa o singular quando a contagem é um', async () => {
      responde(a, Promise.resolve(a.corpo(1)));

      renderPage();

      await waitFor(() => expect(contagem()).toHaveTextContent('1'));
      expect(within(item(a.chave)).getByText(a.singular)).toBeInTheDocument();
      expect(within(item(a.chave)).queryByText(a.plural)).not.toBeInTheDocument();
    });

    it('zero medido é dito por extenso, sem o algarismo e sem o travessão, com a janela', async () => {
      responde(a, Promise.resolve(a.corpo(0)));

      renderPage();

      await waitFor(() => expect(within(item(a.chave)).getByText(a.zero)).toBeInTheDocument());
      expect(contagem()).not.toBeInTheDocument();
      expect(within(item(a.chave)).queryByText('—')).not.toBeInTheDocument();
      // Igualdade do conteúdo inteiro, e não `not.toMatch(/\d/)`: a janela tem
      // um algarismo ("7") que não é contagem. Nenhum "0" cabe entre o rótulo e
      // a frase de ausência.
      expect(item(a.chave).textContent).toBe(`${a.rotulo}${a.zero}últimos 7 dias`);
      expect(janela()).toHaveTextContent('últimos 7 dias');
    });

    it('consulta que falhou diz a razão, oferece nova tentativa e mantém a janela', async () => {
      responde(a, Promise.reject(new Error('apps/inbox fora do ar')));

      renderPage();

      await waitFor(() => expect(within(item(a.chave)).getByText('—')).toBeInTheDocument());
      expect(within(item(a.chave)).getByText(a.razao)).toBeInTheDocument();
      expect(
        within(item(a.chave)).getByRole('button', { name: 'Tentar novamente' }),
      ).toBeInTheDocument();
      expect(within(item(a.chave)).queryByText(a.zero)).not.toBeInTheDocument();
      expect(contagem()).not.toBeInTheDocument();
      expect(janela()).toHaveTextContent('últimos 7 dias');
    });

    it('consulta em andamento não afirma contagem nem falha, e mantém a janela', () => {
      responde(a, new Promise(() => {}));

      renderPage();

      expect(within(item(a.chave)).getByText('Consultando…')).toBeInTheDocument();
      expect(within(item(a.chave)).queryByText('—')).not.toBeInTheDocument();
      expect(within(item(a.chave)).queryByText(a.zero)).not.toBeInTheDocument();
      expect(contagem()).not.toBeInTheDocument();
      expect(janela()).toHaveTextContent('últimos 7 dias');
    });

    // Cenário negativo do critério de atalho: nenhuma listagem conta o mesmo
    // conjunto, então não há para onde apontar — em NENHUM dos quatro estados.
    it.each([
      ['valor', () => Promise.resolve(a.corpo(42))],
      ['zero medido', () => Promise.resolve(a.corpo(0))],
      ['não sei', () => Promise.reject(new Error('falhou'))],
      ['carregando', () => new Promise(() => {})],
    ])('não apresenta atalho no estado %s', async (_estado, resposta) => {
      responde(a, resposta());

      renderPage();

      // Espera o item de catálogo responder, para a asserção não correr antes
      // de qualquer ramo do item de atividade ter sido renderizado.
      await waitFor(() =>
        expect(within(item('agents')).getByText('Nenhum agente')).toBeInTheDocument(),
      );
      expect(within(item(a.chave)).queryByRole('link')).not.toBeInTheDocument();
    });

    it('a nova tentativa refaz só a consulta daquele item, com a janela no instante dela', async () => {
      const user = userEvent.setup();
      responde(a, Promise.reject(new Error('apps/inbox fora do ar')));

      renderPage();

      await waitFor(() => expect(within(item(a.chave)).getByText('—')).toBeInTheDocument());

      const clientes = [
        listAgents,
        listMcpServers,
        listKnowledgeBases,
        listChannels,
        getSessionsSummary,
        getMessagesSummary,
      ].map((f) => vi.mocked(f));
      const antes = clientes.map((c) => c.mock.calls.length);

      responde(a, Promise.resolve(a.corpo(7)));
      await user.click(within(item(a.chave)).getByRole('button', { name: 'Tentar novamente' }));

      await waitFor(() => expect(contagem()).toHaveTextContent('7'));
      const depois = clientes.map((c) => c.mock.calls.length);
      const indice = a.chave === 'sessions' ? 4 : 5;
      depois.forEach((n, i) => expect(n).toBe(i === indice ? antes[i] + 1 : antes[i]));
    });

    it('não carrega texto além de rótulo, situação de contagem e janela', async () => {
      responde(a, Promise.resolve(a.corpo(42)));

      renderPage();

      await waitFor(() => expect(contagem()).toHaveTextContent('42'));
      // Mesma negativa estrutural dos itens de catálogo: igualdade do conteúdo
      // INTEIRO. Qualquer prosa acrescentada quebra a igualdade, seja qual for.
      expect(item(a.chave).textContent).toBe(`${a.rotulo}42${a.plural}últimos 7 dias`);
    });
  });

  // Cenário "A nova tentativa consulta o período atualizado", ao pé da letra:
  // DEPOIS de uma consulta que FALHOU, com o relógio tendo andado. Só `Date` é
  // falso; os timers reais continuam, para o userEvent e o react-query.
  it('a nova tentativa depois de uma falha consulta a janela que termina nela', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    try {
      vi.setSystemTime(new Date('2026-09-18T12:00:00.000Z'));
      const user = userEvent.setup();
      vi.mocked(getSessionsSummary).mockRejectedValue(new Error('apps/inbox fora do ar'));

      renderPage();

      await waitFor(() => expect(within(item('sessions')).getByText('—')).toBeInTheDocument());
      expect(vi.mocked(getSessionsSummary)).toHaveBeenLastCalledWith(
        '2026-09-11T12:00:00.000Z',
        '2026-09-18T12:00:00.000Z',
      );

      vi.setSystemTime(new Date('2026-09-18T12:20:00.000Z'));
      vi.mocked(getSessionsSummary).mockResolvedValue({ startedCount: 5 });
      await user.click(within(item('sessions')).getByRole('button', { name: 'Tentar novamente' }));

      await waitFor(() =>
        expect(
          within(item('sessions')).getByTestId('inventario-sessions-contagem'),
        ).toHaveTextContent('5'),
      );
      expect(vi.mocked(getSessionsSummary)).toHaveBeenLastCalledWith(
        '2026-09-11T12:20:00.000Z',
        '2026-09-18T12:20:00.000Z',
      );
    } finally {
      vi.useRealTimers();
    }
  });

  it('os dois itens de atividade ficam em situações diferentes ao mesmo tempo', async () => {
    vi.mocked(getSessionsSummary).mockResolvedValue({ startedCount: 0 });
    vi.mocked(getMessagesSummary).mockRejectedValue(new Error('apps/inbox fora do ar'));

    renderPage();

    await waitFor(() => expect(within(item('messages')).getByText('—')).toBeInTheDocument());
    expect(within(item('sessions')).getByText('Nenhuma sessão iniciada')).toBeInTheDocument();
    expect(within(item('sessions')).queryByText('—')).not.toBeInTheDocument();
    expect(
      within(item('messages')).queryByText('Nenhuma mensagem recebida'),
    ).not.toBeInTheDocument();
  });

  it('um catálogo e uma atividade sem resposta não afetam os outros quatro', async () => {
    vi.mocked(listKnowledgeBases).mockRejectedValue(new Error('apps/api fora do ar'));
    vi.mocked(getSessionsSummary).mockRejectedValue(new Error('apps/inbox fora do ar'));
    vi.mocked(listAgents).mockResolvedValue(comIds(agent, 3));
    vi.mocked(listMcpServers).mockResolvedValue(comIds(mcpServer, 2));
    vi.mocked(listChannels).mockResolvedValue(comIds(channel, 1));
    vi.mocked(getMessagesSummary).mockResolvedValue({ inboundCount: 128 });

    renderPage();

    await waitFor(() => expect(within(item('sessions')).getByText('—')).toBeInTheDocument());
    expect(within(item('knowledge-bases')).getByText('—')).toBeInTheDocument();
    expect(within(item('agents')).getByTestId('inventario-agents-contagem')).toHaveTextContent('3');
    expect(
      within(item('mcp-servers')).getByTestId('inventario-mcp-servers-contagem'),
    ).toHaveTextContent('2');
    expect(within(item('channels')).getByTestId('inventario-channels-contagem')).toHaveTextContent(
      '1',
    );
    expect(within(item('messages')).getByTestId('inventario-messages-contagem')).toHaveTextContent(
      '128',
    );
  });

  it('a contagem de sessões é a da rota agregada, sem consultar a listagem por canal', async () => {
    // Os canais existem e responderiam. Se a tela recontasse somando as sessões
    // de cada canal, `listChannelSessions` seria chamada — é a apuração paralela
    // que o requisito proíbe (design.md, D2).
    vi.mocked(listChannels).mockResolvedValue(comIds(channel, 2));
    vi.mocked(getSessionsSummary).mockResolvedValue({ startedCount: 42 });

    renderPage();

    await waitFor(() =>
      expect(
        within(item('sessions')).getByTestId('inventario-sessions-contagem'),
      ).toHaveTextContent('42'),
    );
    expect(vi.mocked(getSessionsSummary)).toHaveBeenCalledTimes(1);
    expect(vi.mocked(listChannelSessions)).not.toHaveBeenCalled();
  });
});
