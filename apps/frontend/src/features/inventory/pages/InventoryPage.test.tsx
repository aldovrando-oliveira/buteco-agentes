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
});
