import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { MemoryRouter, Route, Routes } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseDetailPage } from './KnowledgeBaseDetailPage';
import {
  ApiError,
  activateKnowledgeBase,
  deactivateKnowledgeBase,
  getKnowledgeBase,
} from '../api/knowledgeBasesApi';
import { listAgents } from '../../agents/api/agentsApi';
import type { KnowledgeBase } from '../types/knowledgeBase';
import type { Agent } from '../../agents/types/agent';

vi.mock('../api/knowledgeBasesApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeBasesApi')>();
  return {
    ...actual,
    getKnowledgeBase: vi.fn(),
    activateKnowledgeBase: vi.fn(),
    deactivateKnowledgeBase: vi.fn(),
  };
});

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

function agent(overrides: Partial<Agent>): Agent {
  return {
    id: 'agent-1',
    name: 'Atendimento Financeiro',
    instructions: 'Você é um atendente.',
    isActive: true,
    provider: 'openai',
    model: 'gpt-5.6-sol',
    description: null,
    skills: [],
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    mcpServers: [],
    delegatesTo: [],
    knowledgeBases: [],
    a2a: null,
    ...overrides,
  };
}

const knowledgeBase: KnowledgeBase = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Políticas de Cobrança',
  description: 'Regras de negociação, prazos e faixas de desconto.',
  isActive: true,
  createdAt: '2026-09-01T10:00:00Z',
  updatedAt: '2026-09-02T11:00:00Z',
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <Notifications />
        <MemoryRouter initialEntries={[`/knowledge-bases/${knowledgeBase.id}`]}>
          <Routes>
            <Route path="/knowledge-bases/:id" element={<KnowledgeBaseDetailPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('KnowledgeBaseDetailPage', () => {
  beforeEach(() => {
    vi.mocked(getKnowledgeBase).mockReset();
    vi.mocked(activateKnowledgeBase).mockReset();
    vi.mocked(deactivateKnowledgeBase).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
  });

  it('exibe indicador de carregamento enquanto a base não chega', () => {
    vi.mocked(getKnowledgeBase).mockReturnValue(new Promise(() => {}));

    renderPage();

    expect(screen.getByText('Carregando base de conhecimento...')).toBeInTheDocument();
  });

  it('exibe nome e badge de estado', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByRole('heading', { name: knowledgeBase.name })).toBeInTheDocument();
    expect(screen.getByText('Ativa')).toBeInTheDocument();
  });

  // O protótipo não tem datas nesta tela — "Criado em" aparece só no detalhe do
  // agente e no do servidor MCP. O card que existia aqui era acréscimo meu, sem
  // decisão registrada (design.md, D20).
  it('não exibe card de datas', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    await screen.findByRole('heading', { name: knowledgeBase.name });
    expect(screen.queryByText('Criado em')).not.toBeInTheDocument();
    expect(screen.queryByText('Atualizado em')).not.toBeInTheDocument();
  });

  it('lista os agentes que consultam a base', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(listAgents).mockResolvedValue([
      agent({ id: 'a1', knowledgeBases: [{ id: knowledgeBase.id, name: knowledgeBase.name }] }),
    ]);

    renderPage();

    expect(
      await screen.findByRole('link', { name: 'Atendimento Financeiro' }),
    ).toHaveAttribute('href', '/agents/a1');
  });

  it('informa quando nenhum agente consulta a base', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByTestId('agents-empty')).toHaveTextContent(
      'Nenhum agente consulta esta base.',
    );
  });

  it('falha ao carregar agentes não impede o detalhe de renderizar', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(listAgents).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(await screen.findByRole('heading', { name: knowledgeBase.name })).toBeInTheDocument();
    expect(await screen.findByTestId('agents-unavailable')).toBeInTheDocument();
  });

  it('oferece volta para a listagem de conhecimento', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByRole('link', { name: /bases de conhecimento/i })).toHaveAttribute(
      'href',
      '/knowledge-bases',
    );
  });

  it('oferece Editar apontando para a edição da base', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByRole('link', { name: 'Editar' })).toHaveAttribute(
      'href',
      `/knowledge-bases/${knowledgeBase.id}/edit`,
    );
  });

  it('exibe a descrição em card próprio', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByTestId('knowledge-base-description')).toHaveTextContent(
      knowledgeBase.description,
    );
    expect(screen.getByText(/texto lido pelo modelo/i)).toBeInTheDocument();
  });

  // A descrição NÃO é o subtítulo do cabeçalho: como subtítulo ela lê como texto
  // decorativo, e ela é campo de runtime (design.md, D7). Sem `showDescription`
  // falso, o DetailHeader renderizaria "Sem descrição." aqui — que afirmaria o
  // contrário do que a API garante.
  it('não usa a descrição como subtítulo do cabeçalho', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    const heading = await screen.findByRole('heading', { name: knowledgeBase.name });
    expect(screen.getAllByText(knowledgeBase.description)).toHaveLength(1);
    expect(heading.parentElement?.parentElement?.textContent).not.toContain(
      knowledgeBase.description,
    );
  });

  it('não afirma ausência de descrição no cabeçalho', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    await screen.findByRole('heading', { name: knowledgeBase.name });
    expect(screen.queryByText('Sem descrição.')).not.toBeInTheDocument();
  });

  it('informa que a gestão de documentos chega depois, sem afirmar base vazia', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByTestId('documents-placeholder')).toBeInTheDocument();
    expect(screen.queryByText(/nenhum documento/i)).not.toBeInTheDocument();
  });

  // Asserção negativa: as duas abas do protótipo pertencem à 5a-2 e à 5c, e uma
  // barra com uma aba só afirmaria estrutura que a tela não tem (design.md, D3).
  it('não apresenta controle de abas', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    await screen.findByRole('heading', { name: knowledgeBase.name });
    expect(screen.queryAllByRole('tab')).toHaveLength(0);
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
    expect(screen.queryByText(/diagnóstico do índice/i)).not.toBeInTheDocument();
  });

  it('desativar passa por confirmação antes de qualquer requisição', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Desativar' }));

    // O texto "Confirmar desativação" é ao mesmo tempo título do modal e rótulo
    // do botão, então a asserção é sobre o diálogo e a cópia dele.
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/deixarão de consultá-la/i)).toBeInTheDocument();
    expect(deactivateKnowledgeBase).not.toHaveBeenCalled();
  });

  it('desativa só após confirmar', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(deactivateKnowledgeBase).mockResolvedValue({ ...knowledgeBase, isActive: false });

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Confirmar desativação' }));

    expect(deactivateKnowledgeBase).toHaveBeenCalledWith(knowledgeBase.id);
  });

  it('cancelar a confirmação não desativa', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    expect(deactivateKnowledgeBase).not.toHaveBeenCalled();
  });

  it('ativar é imediato, sem confirmação', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue({ ...knowledgeBase, isActive: false });
    vi.mocked(activateKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Ativar' }));

    expect(activateKnowledgeBase).toHaveBeenCalledWith(knowledgeBase.id);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('informa base não encontrada, com volta para a listagem', async () => {
    vi.mocked(getKnowledgeBase).mockRejectedValue(new ApiError(404, 'Não encontrado'));

    renderPage();

    expect(await screen.findByText('Base de conhecimento não encontrada.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Voltar para as bases' })).toHaveAttribute(
      'href',
      '/knowledge-bases',
    );
  });

  it('informa falha genérica de carregamento', async () => {
    vi.mocked(getKnowledgeBase).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(
      await screen.findByText('Não foi possível carregar a base de conhecimento.'),
    ).toBeInTheDocument();
  });
});
