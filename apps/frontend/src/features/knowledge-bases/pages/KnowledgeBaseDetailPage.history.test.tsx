import { StrictMode, type ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseDetailPage } from './KnowledgeBaseDetailPage';
import {
  ApiError,
  deactivateKnowledgeBase,
  deleteKnowledgeBase,
  getKnowledgeBase,
} from '../api/knowledgeBasesApi';
import { listAgents } from '../../agents/api/agentsApi';
import { listKnowledgeIndexDiagnostics } from '../api/knowledgeIndexApi';
import { listKnowledgeDocumentEvents, listKnowledgeDocuments } from '../api/knowledgeDocumentsApi';
import type { KnowledgeBase } from '../types/knowledgeBase';
import type {
  KnowledgeDocumentEvent,
  KnowledgeDocumentEventPage,
} from '../types/knowledgeDocumentEvent';

// A aba Histórico no detalhe da base (design.md da change frontend-aba-historico;
// requisitos de knowledge-document-history-ui e "Detalhe da base de
// conhecimento" de knowledge-base-catalog-ui). Mock de módulo, como
// KnowledgeBaseDetailPage.test.tsx: as afirmações de requisição são sobre
// `listKnowledgeDocumentEvents` — com que cursor e quantas vezes.

vi.mock('../api/knowledgeBasesApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeBasesApi')>();
  return {
    ...actual,
    getKnowledgeBase: vi.fn(),
    deactivateKnowledgeBase: vi.fn(),
    deleteKnowledgeBase: vi.fn(),
  };
});

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

vi.mock('../api/knowledgeIndexApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeIndexApi')>();
  return { ...actual, listKnowledgeIndexDiagnostics: vi.fn() };
});

vi.mock('../api/knowledgeDocumentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeDocumentsApi')>();
  return {
    ...actual,
    listKnowledgeDocuments: vi.fn(),
    listKnowledgeDocumentEvents: vi.fn(),
  };
});

const BASE_ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

const manualBase: KnowledgeBase = {
  id: BASE_ID,
  name: 'Processos Butecando',
  description: 'Compras, contratação e agenda de atrações.',
  isActive: true,
  createdAt: '2026-09-01T10:00:00Z',
  updatedAt: '2026-09-02T11:00:00Z',
  contentMode: 'Manual',
  syncSource: null,
  syncState: null,
};

function evento(overrides: Partial<KnowledgeDocumentEvent> = {}): KnowledgeDocumentEvent {
  return {
    id: 'e-1',
    documentId: 'doc-1',
    documentTitle: 'Política de trocas',
    type: 'Created',
    contentChanged: null,
    titleChanged: null,
    author: 'operator',
    occurredAt: '2026-09-26T12:00:00Z',
    ...overrides,
  };
}

const FIRST_PAGE: KnowledgeDocumentEventPage = {
  items: [evento({ id: 'p1', documentTitle: 'Da primeira página' })],
  nextCursor: 'cursor-1',
};
const SECOND_PAGE: KnowledgeDocumentEventPage = {
  items: [
    evento({ id: 'p2', documentTitle: 'Da segunda página', occurredAt: '2026-09-23T12:00:00Z' }),
  ],
  nextCursor: null,
};

// Responde pela posição do cursor, como a rota: sem cursor, a primeira página;
// com o cursor da primeira, a segunda.
function serveTwoPages() {
  vi.mocked(listKnowledgeDocumentEvents).mockImplementation((_id, cursor) =>
    Promise.resolve(cursor === 'cursor-1' ? SECOND_PAGE : FIRST_PAGE),
  );
}

// `strict`: monta como o `main.tsx` monta, em `<StrictMode>`, que roda a limpeza
// dos efeitos logo depois da montagem.
function renderPage(search = '', { strict = false } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/knowledge-bases/:id', element: <KnowledgeBaseDetailPage /> },
      { path: '/knowledge-bases', element: <p>listagem de bases</p> },
    ],
    { initialEntries: [`/knowledge-bases/${BASE_ID}${search}`] },
  );

  const tree: ReactNode = (
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <Notifications />
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>
  );
  render(strict ? <StrictMode>{tree}</StrictMode> : tree);

  return router;
}

function historyTab() {
  return screen.getByRole('tab', { name: 'Histórico' });
}

async function loadTwoPages(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByText('Da primeira página');
  await user.click(screen.getByRole('button', { name: 'Carregar mais' }));
  await screen.findByText('Da segunda página');
}

beforeEach(() => {
  vi.mocked(getKnowledgeBase).mockReset().mockResolvedValue(manualBase);
  vi.mocked(deactivateKnowledgeBase).mockReset();
  vi.mocked(deleteKnowledgeBase).mockReset();
  vi.mocked(listAgents).mockReset().mockResolvedValue([]);
  vi.mocked(listKnowledgeDocuments).mockReset().mockResolvedValue([]);
  vi.mocked(listKnowledgeIndexDiagnostics).mockReset().mockResolvedValue([]);
  vi.mocked(listKnowledgeDocumentEvents)
    .mockReset()
    .mockResolvedValue({ items: [evento()], nextCursor: null });
});

describe('a aba no endereço', () => {
  it('selecionar Histórico leva a ?tab=historico e lista os eventos', async () => {
    const user = userEvent.setup();
    const router = renderPage();

    await user.click(await screen.findByRole('tab', { name: 'Histórico' }));

    await waitFor(() => expect(router.state.location.search).toBe('?tab=historico'));
    expect(await screen.findByText('Política de trocas')).toBeInTheDocument();
  });

  it('abrir ?tab=historico seleciona a aba, e a primeira página é pedida sem cursor', async () => {
    renderPage('?tab=historico');

    await screen.findByText('Política de trocas');
    expect(historyTab()).toHaveAttribute('aria-selected', 'true');
    expect(listKnowledgeDocumentEvents).toHaveBeenCalledTimes(1);
    expect(listKnowledgeDocumentEvents).toHaveBeenCalledWith(BASE_ID, null);
  });

  // Mantine 9.4.2: `activateTabWithKeyboard` e `loop` ligados por padrão
  // (Tabs.mjs:26-27; tarefa 1.3) — a seta seleciona, e não só foca.
  it('a seta para a direita em Documentos seleciona Histórico', async () => {
    const user = userEvent.setup();
    const router = renderPage();

    const documents = await screen.findByRole('tab', { name: /documentos/i });
    documents.focus();
    await user.keyboard('{ArrowRight}');

    await waitFor(() => expect(historyTab()).toHaveAttribute('aria-selected', 'true'));
    expect(router.state.location.search).toBe('?tab=historico');
  });

  // A rota não tem total: um número aqui seria a contagem das páginas pedidas.
  it('a aba Histórico não tem contador, mesmo com eventos carregados', async () => {
    renderPage('?tab=historico');

    await screen.findByText('Política de trocas');
    expect(historyTab().textContent).toBe('Histórico');
  });
});

// D6: os três recomeços pedem UMA página, sem cursor, e descartam as carregadas.
describe('recomeço da primeira página', () => {
  // Conferência visual R1-1: com o painel em StrictMode, a entrada na aba pedia
  // DUAS vezes — a limpeza do efeito de saída roda logo após a montagem, e
  // cancelava a busca da primeira página, que o observador refazia. Sair da aba
  // só apara, e só cancela um "Carregar mais" em voo.
  it('entrar na aba pede uma vez, também em StrictMode', async () => {
    renderPage('?tab=historico', { strict: true });

    await screen.findByText('Política de trocas');
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(listKnowledgeDocumentEvents).toHaveBeenCalledTimes(1);
  });

  it('voltar à aba recomeça da primeira página', async () => {
    const user = userEvent.setup();
    serveTwoPages();
    renderPage('?tab=historico');
    await loadTwoPages(user);

    // Zerado ANTES de sair: sair da aba não pode pedir nada (só apara o cache),
    // e a volta pede uma página.
    vi.mocked(listKnowledgeDocumentEvents).mockClear();
    await user.click(screen.getByRole('tab', { name: /documentos/i }));
    await screen.findByTestId('documents-empty');
    expect(listKnowledgeDocumentEvents).not.toHaveBeenCalled();
    await user.click(historyTab());

    await screen.findByText('Da primeira página');
    await waitFor(() => expect(listKnowledgeDocumentEvents).toHaveBeenCalledTimes(1));
    expect(listKnowledgeDocumentEvents).toHaveBeenCalledWith(BASE_ID, null);
    expect(screen.queryByText('Da segunda página')).not.toBeInTheDocument();
  });

  it('Atualizar relê só a primeira página, sem cursor', async () => {
    const user = userEvent.setup();
    serveTwoPages();
    renderPage('?tab=historico');
    await loadTwoPages(user);

    vi.mocked(listKnowledgeDocumentEvents).mockClear();
    await user.click(screen.getByRole('button', { name: 'Atualizar' }));

    await waitFor(() => expect(listKnowledgeDocumentEvents).toHaveBeenCalledTimes(1));
    expect(listKnowledgeDocumentEvents).toHaveBeenCalledWith(BASE_ID, null);
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Atualizar' })).not.toHaveAttribute('data-loading'),
    );
    expect(screen.getByText('Da primeira página')).toBeInTheDocument();
    expect(screen.queryByText('Da segunda página')).not.toBeInTheDocument();
    expect(listKnowledgeDocumentEvents).toHaveBeenCalledTimes(1);
  });
});

// D6: a chave do histórico fica FORA do prefixo ['knowledge-bases']. Desativar
// invalida esse prefixo sem `exact` (useKnowledgeBases.ts, cacheUpdatedKnowledgeBase);
// dentro dele, a consulta infinita aberta seria relida inteira.
describe('mudar a base não relê o histórico', () => {
  it('desativar a base com duas páginas carregadas não pede eventos', async () => {
    const user = userEvent.setup();
    serveTwoPages();
    vi.mocked(deactivateKnowledgeBase).mockResolvedValue({ ...manualBase, isActive: false });
    renderPage('?tab=historico');
    await loadTwoPages(user);

    vi.mocked(listKnowledgeDocumentEvents).mockClear();
    vi.mocked(getKnowledgeBase)
      .mockClear()
      .mockResolvedValue({ ...manualBase, isActive: false });
    await user.click(screen.getByRole('button', { name: 'Desativar' }));
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', {
        name: 'Confirmar desativação',
      }),
    );

    // Espera a invalidação por prefixo ter efeito: a base é relida por ela. A
    // releitura do histórico, se a chave estivesse no prefixo, sairia no mesmo
    // lote.
    await screen.findByRole('button', { name: 'Ativar' });
    await waitFor(() => expect(getKnowledgeBase).toHaveBeenCalled());
    expect(listKnowledgeDocumentEvents).not.toHaveBeenCalled();
    expect(screen.getByText('Da segunda página')).toBeInTheDocument();
  });

  // O 409 da exclusão relê só a consulta da base (refetch), e não invalida
  // prefixo: a ausência passa com qualquer chave, e fica como negativa, não
  // como guarda (D6).
  it('o 409 da exclusão numa base inativa não pede eventos', async () => {
    const user = userEvent.setup();
    serveTwoPages();
    vi.mocked(getKnowledgeBase).mockResolvedValue({ ...manualBase, isActive: false });
    vi.mocked(deleteKnowledgeBase).mockRejectedValue(
      new ApiError(409, 'x', { title: 'x', status: 409, code: 'knowledge-base-active' }),
    );
    renderPage('?tab=historico');
    await loadTwoPages(user);

    vi.mocked(listKnowledgeDocumentEvents).mockClear();
    vi.mocked(getKnowledgeBase).mockClear();
    await user.click(screen.getByRole('button', { name: 'Excluir a base' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Excluir base' }));

    await within(dialog).findByRole('alert');
    await waitFor(() => expect(getKnowledgeBase).toHaveBeenCalled());
    expect(listKnowledgeDocumentEvents).not.toHaveBeenCalled();
  });
});

// D5: a proibição de exclu/remov/apag continua no card de origem com o
// `Excluído` do histórico na mesma tela. A varredura fica sobre o card, e não
// sobre a página.
describe('palavras proibidas', () => {
  it('o painel exibe Excluído e o card de origem continua sem exclu, remov nem apag', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue({
      ...manualBase,
      isActive: false,
      contentMode: 'Synced',
      syncSource: {
        provider: 'google-drive',
        folderId: 'pasta-1',
        folderName: 'Políticas',
        folderUrl: 'https://drive.google.com/drive/folders/pasta-1',
      },
      syncState: {
        lastCompletedAt: '2026-09-30T10:00:00Z',
        lastFinishedAt: '2026-10-01T10:00:00Z',
        failingSince: '2026-10-01T10:00:00Z',
        lastError: { code: 'access-denied', detail: 'sync@exemplo.iam.gserviceaccount.com' },
        ignoredFiles: [],
      },
    });
    vi.mocked(listKnowledgeDocumentEvents).mockResolvedValue({
      items: [evento({ type: 'Deleted', author: 'service:connectors' })],
      nextCursor: null,
    });
    renderPage('?tab=historico');

    const panel = await screen.findByRole('tabpanel');
    expect(await within(panel).findByText('Excluído')).toBeInTheDocument();
    const card = screen.getByTestId('sync-origin-card');
    expect(within(card).getByTestId('sync-failure-alert')).toHaveTextContent('Sem acesso à pasta');
    expect(card).not.toHaveTextContent(/exclu|remov|apag/i);
  });
});
