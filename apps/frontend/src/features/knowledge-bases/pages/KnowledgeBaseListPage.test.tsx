import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseListPage } from './KnowledgeBaseListPage';
import { listKnowledgeBases } from '../api/knowledgeBasesApi';
import { listAgents } from '../../agents/api/agentsApi';
import type { KnowledgeBase } from '../types/knowledgeBase';

vi.mock('../api/knowledgeBasesApi', () => ({
  listKnowledgeBases: vi.fn(),
}));

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

function base(overrides: Partial<KnowledgeBase>): KnowledgeBase {
  return {
    id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    name: 'Políticas de Cobrança',
    description: 'Regras de negociação, prazos e faixas de desconto.',
    isActive: true,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-02T00:00:00Z',
    ...overrides,
  };
}

const cobranca = base({});
const rotinas = base({
  id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  name: 'Rotinas Internas',
  description: 'Procedimentos de abertura e fechamento.',
  isActive: false,
});

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <KnowledgeBaseListPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('KnowledgeBaseListPage', () => {
  beforeEach(() => {
    vi.mocked(listKnowledgeBases).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
  });

  it('exibe indicador de carregamento enquanto a lista não chega', () => {
    vi.mocked(listKnowledgeBases).mockReturnValue(new Promise(() => {}));

    renderPage();

    expect(screen.getByText('Carregando bases de conhecimento...')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Nova base' })).toBeInTheDocument();
  });

  it('renderiza a lista quando existem bases cadastradas', async () => {
    vi.mocked(listKnowledgeBases).mockResolvedValue([cobranca, rotinas]);

    renderPage();

    expect(await screen.findByRole('link', { name: cobranca.name })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: rotinas.name })).toBeInTheDocument();
  });

  it('inclui bases inativas na listagem', async () => {
    vi.mocked(listKnowledgeBases).mockResolvedValue([rotinas]);

    renderPage();

    expect(await screen.findByRole('link', { name: rotinas.name })).toBeInTheDocument();
    expect(screen.getByText('Inativa')).toBeInTheDocument();
  });

  it('exibe estado de erro quando a lista falha ao carregar', async () => {
    vi.mocked(listKnowledgeBases).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(
      await screen.findByText('Não foi possível carregar as bases de conhecimento.'),
    ).toBeInTheDocument();
  });

  it('explica o que é uma base no catálogo vazio e oferece criar a primeira', async () => {
    vi.mocked(listKnowledgeBases).mockResolvedValue([]);

    renderPage();

    const empty = await screen.findByTestId('empty-catalog');
    expect(
      within(empty).getByText('Nenhuma base de conhecimento cadastrada ainda.'),
    ).toBeInTheDocument();
    expect(within(empty).getByText(/documentos markdown/i)).toBeInTheDocument();
    expect(within(empty).getByRole('link', { name: 'Criar a primeira base' })).toHaveAttribute(
      'href',
      '/knowledge-bases/new',
    );
  });

  it('não oferece busca nem filtro quando o catálogo está vazio', async () => {
    vi.mocked(listKnowledgeBases).mockResolvedValue([]);

    renderPage();

    await screen.findByTestId('empty-catalog');
    expect(screen.queryByLabelText('Buscar por nome ou descrição')).not.toBeInTheDocument();
  });

  it('encontra por nome mesmo sem acento no termo digitado', async () => {
    const user = userEvent.setup();
    vi.mocked(listKnowledgeBases).mockResolvedValue([cobranca, rotinas]);

    renderPage();
    await screen.findByRole('link', { name: cobranca.name });

    await user.type(screen.getByLabelText('Buscar por nome ou descrição'), 'cobranca');

    expect(screen.getByRole('link', { name: cobranca.name })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: rotinas.name })).not.toBeInTheDocument();
  });

  it('encontra por termo presente apenas na descrição', async () => {
    const user = userEvent.setup();
    vi.mocked(listKnowledgeBases).mockResolvedValue([cobranca, rotinas]);

    renderPage();
    await screen.findByRole('link', { name: cobranca.name });

    await user.type(screen.getByLabelText('Buscar por nome ou descrição'), 'fechamento');

    expect(screen.getByRole('link', { name: rotinas.name })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: cobranca.name })).not.toBeInTheDocument();
  });

  it('distingue busca sem resultado de catálogo vazio', async () => {
    const user = userEvent.setup();
    vi.mocked(listKnowledgeBases).mockResolvedValue([cobranca]);

    renderPage();
    await screen.findByRole('link', { name: cobranca.name });

    await user.type(screen.getByLabelText('Buscar por nome ou descrição'), 'entrega');

    expect(screen.getByText('Nenhuma base corresponde à busca.')).toBeInTheDocument();
    expect(screen.queryByTestId('empty-catalog')).not.toBeInTheDocument();
  });

  it('filtra por inativas escondendo as ativas', async () => {
    const user = userEvent.setup();
    vi.mocked(listKnowledgeBases).mockResolvedValue([cobranca, rotinas]);

    renderPage();
    await screen.findByRole('link', { name: cobranca.name });

    await user.click(screen.getByRole('radio', { name: 'Inativas' }));

    expect(screen.getByRole('link', { name: rotinas.name })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: cobranca.name })).not.toBeInTheDocument();
  });

  it('exibe os agentes que consultam cada base', async () => {
    vi.mocked(listKnowledgeBases).mockResolvedValue([cobranca]);
    vi.mocked(listAgents).mockResolvedValue([
      {
        id: 'a1',
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
        knowledgeBases: [{ id: cobranca.id, name: cobranca.name }],
        a2a: null,
      },
    ]);

    renderPage();

    expect(await screen.findByText('Atendimento Financeiro')).toBeInTheDocument();
  });

  it('falha ao carregar agentes não impede a listagem', async () => {
    vi.mocked(listKnowledgeBases).mockResolvedValue([cobranca]);
    vi.mocked(listAgents).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(await screen.findByRole('link', { name: cobranca.name })).toBeInTheDocument();
    expect(screen.queryByText('Nenhum agente')).not.toBeInTheDocument();
  });

  it('filtra por ativas escondendo as inativas', async () => {
    const user = userEvent.setup();
    vi.mocked(listKnowledgeBases).mockResolvedValue([cobranca, rotinas]);

    renderPage();
    await screen.findByRole('link', { name: cobranca.name });

    await user.click(screen.getByRole('radio', { name: 'Ativas' }));

    expect(screen.getByRole('link', { name: cobranca.name })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: rotinas.name })).not.toBeInTheDocument();
  });

  // Asserção negativa: a quarta opção do protótipo depende de estado de
  // indexação agregado por base, que a API não devolve (design.md, D9).
  it('oferece exatamente três opções de filtro, sem falha de indexação', async () => {
    vi.mocked(listKnowledgeBases).mockResolvedValue([cobranca]);

    renderPage();
    await screen.findByRole('link', { name: cobranca.name });

    expect(screen.getAllByRole('radio').map((option) => option.getAttribute('value'))).toEqual([
      'todas',
      'ativas',
      'inativas',
    ]);
    expect(screen.queryByRole('radio', { name: /falha/i })).not.toBeInTheDocument();
  });
});
