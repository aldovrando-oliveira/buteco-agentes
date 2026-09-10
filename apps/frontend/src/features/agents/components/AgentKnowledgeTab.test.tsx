import { beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentKnowledgeTab } from './AgentKnowledgeTab';
import { replaceAgentKnowledgeBases } from '../api/agentsApi';
import type { Agent } from '../types/agent';
import type { KnowledgeBase } from '../../knowledge-bases/types/knowledgeBase';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return { ...actual, replaceAgentKnowledgeBases: vi.fn() };
});

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>();
  return { ...actual, notifications: { ...actual.notifications, show: vi.fn() } };
});

function base(
  overrides: Partial<KnowledgeBase> & Pick<KnowledgeBase, 'id' | 'name'>,
): KnowledgeBase {
  return {
    description: 'Descrição da base.',
    isActive: true,
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

const cardapio = base({ id: 'k1', name: 'Cardápio', description: 'Pratos e preços.' });
const cobranca = base({ id: 'k2', name: 'Cobrança', description: 'Regras de negociação.' });
const rotinas = base({
  id: 'k3',
  name: 'Rotinas Internas',
  description: 'Processos do time.',
  isActive: false,
});

const catalog = [cardapio, cobranca, rotinas];

const agent: Agent = {
  id: '11111111-1111-1111-1111-111111111111',
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
  knowledgeBases: [{ id: cardapio.id, name: cardapio.name }],
  a2a: null,
};

function renderTab(overrides?: { agent?: Agent; catalog?: KnowledgeBase[] }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      {
        path: '/agents/:id',
        element: (
          <AgentKnowledgeTab
            agent={overrides?.agent ?? agent}
            catalog={overrides?.catalog ?? catalog}
          />
        ),
      },
      { path: '/knowledge-bases/new', element: <p>Nova base</p> },
    ],
    { initialEntries: [`/agents/${agent.id}?tab=conhecimento`] },
  );

  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );
}

async function abrirModalEVincular(user: ReturnType<typeof userEvent.setup>, id: string) {
  await user.click(screen.getAllByRole('button', { name: 'Vincular base' })[0]);
  const linha = await screen.findByTestId(`link-modal-row-${id}`);
  await user.click(within(linha).getByRole('button', { name: 'Vincular' }));
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('AgentKnowledgeTab', () => {
  it('lista as bases vinculadas com nome como link para o detalhe e descrição', () => {
    renderTab();

    const linha = screen.getByTestId(`knowledge-row-${cardapio.id}`);
    expect(within(linha).getByRole('link', { name: 'Cardápio' })).toHaveAttribute(
      'href',
      `/knowledge-bases/${cardapio.id}`,
    );
    expect(within(linha).getByText('Pratos e preços.')).toBeInTheDocument();
  });

  it('exibe o total do catálogo no cabeçalho do card', () => {
    renderTab();

    expect(screen.getByText('3 bases no catálogo')).toBeInTheDocument();
  });

  it('agente sem nenhuma base vinculada exibe o vazio explicativo, não uma linha de texto', () => {
    renderTab({ agent: { ...agent, knowledgeBases: [] } });

    const vazio = screen.getByTestId('knowledge-empty');
    expect(vazio).toHaveTextContent('Nenhuma base vinculada');
    expect(vazio).toHaveTextContent(/responde só com o system prompt/i);
    expect(within(vazio).getByRole('button', { name: 'Vincular base' })).toBeInTheDocument();
    expect(screen.getByTestId('knowledge-summary')).toHaveTextContent(
      'Nenhuma base vinculada — este agente não consulta conhecimento',
    );
  });

  it('avisa que base vinculada e inativa não é consultada', () => {
    renderTab({
      agent: { ...agent, knowledgeBases: [{ id: rotinas.id, name: rotinas.name }] },
    });

    expect(screen.getByTestId(`knowledge-inactive-${rotinas.id}`)).toHaveTextContent(
      'Base inativa: o agente não a consulta enquanto ela estiver desativada.',
    );
  });

  // Par negativo: sem ele, um componente que avisasse em TODAS as linhas
  // passaria no cenário acima.
  it('não avisa nada quando a base vinculada está ativa', () => {
    renderTab();

    expect(screen.queryByTestId(`knowledge-inactive-${cardapio.id}`)).not.toBeInTheDocument();
  });

  it('vincular pelo modal e salvar envia o conjunto completo resultante', async () => {
    const user = userEvent.setup();
    vi.mocked(replaceAgentKnowledgeBases).mockResolvedValue({
      ...agent,
      knowledgeBases: [
        { id: cardapio.id, name: cardapio.name },
        { id: cobranca.id, name: cobranca.name },
      ],
    });
    renderTab();

    await abrirModalEVincular(user, cobranca.id);
    await user.click(screen.getByRole('button', { name: 'Concluir' }));
    await user.click(screen.getByRole('button', { name: 'Salvar bases de conhecimento' }));

    await waitFor(() =>
      expect(replaceAgentKnowledgeBases).toHaveBeenCalledWith(agent.id, [cardapio.id, cobranca.id]),
    );
  });

  it('desvincular e salvar envia o conjunto sem a base removida', async () => {
    const user = userEvent.setup();
    vi.mocked(replaceAgentKnowledgeBases).mockResolvedValue({
      ...agent,
      knowledgeBases: [{ id: cobranca.id, name: cobranca.name }],
    });
    renderTab({
      agent: {
        ...agent,
        knowledgeBases: [
          { id: cardapio.id, name: cardapio.name },
          { id: cobranca.id, name: cobranca.name },
        ],
      },
    });

    const linha = screen.getByTestId(`knowledge-row-${cardapio.id}`);
    await user.click(within(linha).getByRole('button', { name: 'Desvincular' }));
    await user.click(screen.getByRole('button', { name: 'Salvar bases de conhecimento' }));

    await waitFor(() =>
      expect(replaceAgentKnowledgeBases).toHaveBeenCalledWith(agent.id, [cobranca.id]),
    );
  });

  it('remover todas as bases envia conjunto vazio e a aba passa ao estado vazio', async () => {
    const user = userEvent.setup();
    vi.mocked(replaceAgentKnowledgeBases).mockResolvedValue({ ...agent, knowledgeBases: [] });
    renderTab();

    const linha = screen.getByTestId(`knowledge-row-${cardapio.id}`);
    await user.click(within(linha).getByRole('button', { name: 'Desvincular' }));

    expect(screen.getByTestId('knowledge-empty')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Salvar bases de conhecimento' }));

    await waitFor(() => expect(replaceAgentKnowledgeBases).toHaveBeenCalledWith(agent.id, []));
  });

  // Guarda do requisito central: a operação é de conjunto, e alternar uma base
  // é edição de rascunho. Uma requisição por clique seria o desenho do handoff,
  // recusado em D3.
  it('alternar uma base não emite requisição nenhuma', async () => {
    const user = userEvent.setup();
    renderTab();

    const linha = screen.getByTestId(`knowledge-row-${cardapio.id}`);
    await user.click(within(linha).getByRole('button', { name: 'Desvincular' }));
    await abrirModalEVincular(user, cobranca.id);

    expect(replaceAgentKnowledgeBases).not.toHaveBeenCalled();
  });

  it('exibe a barra de alterações não salvas só quando o rascunho difere do gravado', async () => {
    const user = userEvent.setup();
    renderTab();

    expect(screen.queryByTestId('unsaved-changes-bar')).not.toBeInTheDocument();

    const linha = screen.getByTestId(`knowledge-row-${cardapio.id}`);
    await user.click(within(linha).getByRole('button', { name: 'Desvincular' }));

    expect(screen.getByTestId('unsaved-changes-bar')).toBeInTheDocument();
  });

  it('descartar volta exatamente às bases gravadas', async () => {
    const user = userEvent.setup();
    renderTab();

    const linha = screen.getByTestId(`knowledge-row-${cardapio.id}`);
    await user.click(within(linha).getByRole('button', { name: 'Desvincular' }));
    await user.click(screen.getByRole('button', { name: 'Descartar' }));

    expect(screen.getByTestId(`knowledge-row-${cardapio.id}`)).toBeInTheDocument();
    expect(screen.queryByTestId('unsaved-changes-bar')).not.toBeInTheDocument();
  });

  it('falha na gravação preserva o rascunho e não marca linha nenhuma', async () => {
    const user = userEvent.setup();
    vi.mocked(replaceAgentKnowledgeBases).mockRejectedValue(new Error('falhou'));
    renderTab();

    await abrirModalEVincular(user, cobranca.id);
    await user.click(screen.getByRole('button', { name: 'Concluir' }));
    await user.click(screen.getByRole('button', { name: 'Salvar bases de conhecimento' }));

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    // O rascunho continua na tela, inteiro.
    expect(screen.getByTestId(`knowledge-row-${cobranca.id}`)).toBeInTheDocument();
    expect(screen.getByTestId('unsaved-changes-bar')).toBeInTheDocument();
    // Nenhuma linha carrega marca de falha própria: quem falhou foi o conjunto.
    expect(screen.getByTestId(`knowledge-row-${cobranca.id}`)).not.toHaveTextContent(/falh/i);
  });

  it('sucesso notifica e a barra some quando a página traz o agente atualizado', async () => {
    const user = userEvent.setup();
    const atualizado: Agent = {
      ...agent,
      knowledgeBases: [
        { id: cardapio.id, name: cardapio.name },
        { id: cobranca.id, name: cobranca.name },
      ],
    };
    vi.mocked(replaceAgentKnowledgeBases).mockResolvedValue(atualizado);
    renderTab();

    await abrirModalEVincular(user, cobranca.id);
    await user.click(screen.getByRole('button', { name: 'Concluir' }));
    await user.click(screen.getByRole('button', { name: 'Salvar bases de conhecimento' }));

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );

    // A barra compara o rascunho com o vínculo GRAVADO, que chega por
    // propriedade. Quem a faz sumir é a página re-renderizando com o agente
    // atualizado — a mutação escreve em ['agents', id] e o detalhe reage. É o
    // mesmo desenho de AgentToolsTab e AgentDelegationsTab.
    cleanup();
    renderTab({ agent: atualizado });
    expect(screen.queryByTestId('unsaved-changes-bar')).not.toBeInTheDocument();
  });

  // O guarda de verdade do rebase: sem `setSelection(updated.knowledgeBases…)`,
  // uma segunda gravação reenviaria o conjunto que o operador montou, e não o
  // que o servidor devolveu. Aqui a resposta difere do enviado de propósito —
  // com respostas idênticas, os dois comportamentos são indistinguíveis.
  it('gravação seguinte parte do conjunto devolvido pela resposta, não do anterior', async () => {
    const user = userEvent.setup();
    vi.mocked(replaceAgentKnowledgeBases).mockResolvedValue({
      ...agent,
      knowledgeBases: [{ id: cobranca.id, name: cobranca.name }],
    });
    renderTab();

    await abrirModalEVincular(user, cobranca.id);
    await user.click(screen.getByRole('button', { name: 'Concluir' }));
    await user.click(screen.getByRole('button', { name: 'Salvar bases de conhecimento' }));

    await waitFor(() =>
      expect(replaceAgentKnowledgeBases).toHaveBeenNthCalledWith(1, agent.id, [
        cardapio.id,
        cobranca.id,
      ]),
    );

    await abrirModalEVincular(user, rotinas.id);
    await user.click(screen.getByRole('button', { name: 'Concluir' }));
    await user.click(screen.getByRole('button', { name: 'Salvar bases de conhecimento' }));

    await waitFor(() =>
      expect(replaceAgentKnowledgeBases).toHaveBeenNthCalledWith(2, agent.id, [
        cobranca.id,
        rotinas.id,
      ]),
    );
  });

  it('a lista fica ordenada por nome, não pela ordem de escolha', async () => {
    const user = userEvent.setup();
    renderTab({
      agent: { ...agent, knowledgeBases: [{ id: rotinas.id, name: rotinas.name }] },
    });

    await abrirModalEVincular(user, cardapio.id);
    await user.click(screen.getByRole('button', { name: 'Concluir' }));

    const nomes = screen
      .getAllByTestId(/^knowledge-row-/)
      .map((row) => within(row).getByRole('link').textContent);
    expect(nomes).toEqual(['Cardápio', 'Rotinas Internas']);
  });

  // Asserção NEGATIVA: KnowledgeBaseResponse não tem contagem de documentos, e
  // o protótipo exibe "{n} documentos · {k} indexados" na linha. Zerar afirmaria
  // que a contagem foi feita (convenção 13).
  it('a linha não exibe contagem de documentos nem estado de indexação', () => {
    renderTab();

    const linha = screen.getByTestId(`knowledge-row-${cardapio.id}`);
    expect(linha).not.toHaveTextContent(/documento/i);
    expect(linha).not.toHaveTextContent(/indexad/i);
    expect(linha).not.toHaveTextContent(/fragmento/i);
  });
});
