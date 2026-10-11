import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseDetailPage } from './KnowledgeBaseDetailPage';
import { KnowledgeBaseListPage } from './KnowledgeBaseListPage';
import {
  ApiError,
  deleteKnowledgeBase,
  getKnowledgeBase,
  listKnowledgeBaseIndexingSummary,
  listKnowledgeBases,
} from '../api/knowledgeBasesApi';
import { listAgents } from '../../agents/api/agentsApi';
import { listKnowledgeDocuments } from '../api/knowledgeDocumentsApi';
import type { KnowledgeBase } from '../types/knowledgeBase';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';
import type { Agent } from '../../agents/types/agent';

// Exclusão de base no detalhe (design.md da change exclusao-base-painel, D1 a D3;
// requisito "Exclusão de base de conhecimento no detalhe" de
// knowledge-base-catalog-ui). "Nenhuma requisição" é afirmada como
// `deleteKnowledgeBase` não chamada.

vi.mock('../api/knowledgeBasesApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeBasesApi')>();
  return {
    ...actual,
    getKnowledgeBase: vi.fn(),
    listKnowledgeBases: vi.fn(),
    listKnowledgeBaseIndexingSummary: vi.fn(),
    deleteKnowledgeBase: vi.fn(),
  };
});

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

vi.mock('../api/knowledgeDocumentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeDocumentsApi')>();
  return { ...actual, listKnowledgeDocuments: vi.fn() };
});

const BASE_ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const BASE_NAME = 'Políticas de Cobrança';
const SERVER_TITLE = 'TITULO-DO-SERVIDOR';
const SERVER_DETAIL = 'DETALHE-DO-SERVIDOR';

const inactiveBase: KnowledgeBase = {
  id: BASE_ID,
  name: BASE_NAME,
  description: 'Regras de negociação, prazos e faixas de desconto.',
  isActive: false,
  createdAt: '2026-09-01T10:00:00Z',
  updatedAt: '2026-09-02T11:00:00Z',
  contentMode: 'Manual',
  syncSource: null,
  syncState: null,
};

const activeBase: KnowledgeBase = { ...inactiveBase, isActive: true };

const otherBase: KnowledgeBase = {
  ...inactiveBase,
  id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  name: 'Manual do Atendente',
};

function agent(id: string, name: string, knowledgeBaseIds: string[]): Agent {
  return {
    id,
    name,
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
    knowledgeBases: knowledgeBaseIds.map((kbId) => ({ id: kbId, name: 'x' })),
    a2a: null,
  } as Agent;
}

function documento(id: string): KnowledgeDocumentSummary {
  return {
    id,
    knowledgeBaseId: BASE_ID,
    title: `Documento ${id}`,
    sourceType: 'markdown',
    contentLengthBytes: 100,
    indexingStatus: 'Indexed',
    indexedAt: '2026-09-02T03:14:00Z',
    failureReason: null,
    contentRevision: 1,
    fragmentCount: 2,
    indexingAttempts: 1,
    lastAttemptAt: '2026-09-02T03:14:00Z',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-02T00:00:00Z',
  };
}

function problem(status: number, code?: string): ApiError {
  return new ApiError(status, SERVER_TITLE, {
    title: SERVER_TITLE,
    status,
    detail: SERVER_DETAIL,
    ...(code ? { code } : {}),
  });
}

function renderPage(
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
) {
  const router = createMemoryRouter(
    [
      { path: '/knowledge-bases/:id', element: <KnowledgeBaseDetailPage /> },
      { path: '/knowledge-bases', element: <KnowledgeBaseListPage /> },
      { path: '/knowledge-bases/:id/edit', element: <p>edição de base</p> },
    ],
    { initialEntries: [`/knowledge-bases/${BASE_ID}`] },
  );

  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <Notifications />
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );

  return { router, queryClient };
}

// Nome acessível próprio: as linhas da tabela de documentos já têm botões
// "Excluir" (D1, achado da implementação). O texto visível continua `Excluir`.
const DELETE_BASE = 'Excluir a base';

async function deleteButton() {
  return screen.findByRole('button', { name: DELETE_BASE });
}

async function openConfirmation(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await deleteButton());
  return screen.findByRole('dialog', { name: `Excluir "${BASE_NAME}"?` });
}

beforeEach(() => {
  vi.mocked(getKnowledgeBase).mockReset().mockResolvedValue(inactiveBase);
  vi.mocked(listKnowledgeBases).mockReset().mockResolvedValue([otherBase]);
  vi.mocked(listKnowledgeBaseIndexingSummary).mockReset().mockResolvedValue([]);
  vi.mocked(deleteKnowledgeBase).mockReset().mockResolvedValue(undefined);
  vi.mocked(listAgents)
    .mockReset()
    .mockResolvedValue([
      agent('agent-1', 'Atendente de Suporte', [BASE_ID]),
      agent('agent-2', 'Triagem', [BASE_ID]),
      agent('agent-3', 'Cobrança Ativa', [otherBase.id]),
    ]);
  vi.mocked(listKnowledgeDocuments)
    .mockReset()
    .mockResolvedValue([documento('1'), documento('2'), documento('3')]);
});

describe('base ativa: a exclusão não está disponível', () => {
  beforeEach(() => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(activeBase);
  });

  it('o botão está indisponível, o clique não abre a confirmação e nada é chamado', async () => {
    const user = userEvent.setup();
    renderPage();

    const button = await deleteButton();
    expect(button).toHaveTextContent('Excluir');
    expect(button).toHaveAttribute('aria-disabled', 'true');
    // Indisponível, mas focável: é pelo foco que a explicação chega ao teclado (D1).
    expect(button).not.toBeDisabled();

    await user.click(button);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(deleteKnowledgeBase).not.toHaveBeenCalled();
  });

  it('o foco no botão mostra que é preciso desativar antes', async () => {
    const user = userEvent.setup();
    renderPage();

    await deleteButton();
    // Tab até o botão: Editar, Desativar, Excluir.
    while (document.activeElement !== screen.getByRole('button', { name: DELETE_BASE })) {
      await user.tab();
    }

    expect(await screen.findByRole('tooltip')).toHaveTextContent(
      'Só uma base inativa pode ser excluída. Desative a base antes',
    );
  });
});

describe('base inativa: confirmação', () => {
  it('nomeia a base, os agentes e a quantidade de documentos, antes de qualquer chamada', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText('Documento 3');

    const dialog = await openConfirmation(user);

    expect(dialog).toHaveTextContent('A base e os seus 3 documentos são excluídos');
    expect(dialog).toHaveTextContent('Os vínculos com Atendente de Suporte e Triagem saem junto.');
    expect(dialog).not.toHaveTextContent('Cobrança Ativa');
    expect(deleteKnowledgeBase).not.toHaveBeenCalled();
  });

  it('documentos e agentes desconhecidos: nenhum número e nenhum "nenhum agente"', async () => {
    vi.mocked(listKnowledgeDocuments).mockRejectedValue(new ApiError(500, 'x'));
    vi.mocked(listAgents).mockRejectedValue(new Error('rede'));
    const user = userEvent.setup();
    renderPage();
    await screen.findByText(/Não foi possível carregar os documentos/);

    const dialog = await openConfirmation(user);

    expect(dialog).toHaveTextContent('A base e todos os documentos dela são excluídos');
    expect(dialog).toHaveTextContent('Não foi possível conferir os agentes vinculados');
    expect(dialog.textContent).not.toMatch(/\d/);
    expect(dialog).not.toHaveTextContent(/Nenhum agente/i);
    expect(dialog).not.toHaveTextContent(/não tem documentos/i);
  });

  it('base sincronizada diz que a pasta fica livre e o Drive não é tocado', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue({
      ...inactiveBase,
      contentMode: 'Synced',
      syncSource: {
        provider: 'google-drive',
        folderId: 'pasta-1',
        folderName: 'Políticas',
        folderUrl: 'https://drive.google.com/drive/folders/pasta-1',
      },
      syncState: {
        lastCompletedAt: null,
        lastFinishedAt: null,
        failingSince: null,
        lastError: null,
        ignoredFiles: null,
      },
    });
    const user = userEvent.setup();
    renderPage();

    const dialog = await openConfirmation(user);

    expect(dialog).toHaveTextContent(
      'A pasta “Políticas” fica livre para ser usada por outra base.',
    );
    expect(dialog).toHaveTextContent('Os arquivos no Google Drive não são tocados.');
  });

  it('cancelar não chama a rota e mantém o detalhe', async () => {
    const user = userEvent.setup();
    const { router } = renderPage();

    const dialog = await openConfirmation(user);
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(deleteKnowledgeBase).not.toHaveBeenCalled();
    expect(router.state.location.pathname).toBe(`/knowledge-bases/${BASE_ID}`);
  });
});

describe('respostas da rota', () => {
  it('204 volta para a listagem com a notificação, e a base não aparece', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    // A listagem já tinha sido vista com a base: sem a invalidação, ela voltaria do
    // cache.
    queryClient.setQueryData(['knowledge-bases'], [inactiveBase, otherBase]);
    const user = userEvent.setup();
    const { router } = renderPage(queryClient);

    const dialog = await openConfirmation(user);
    await user.click(within(dialog).getByRole('button', { name: 'Excluir base' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/knowledge-bases'));
    expect(deleteKnowledgeBase).toHaveBeenCalledWith(BASE_ID);
    expect(await screen.findByText(`"${BASE_NAME}" foi excluída.`)).toBeInTheDocument();
    // A listagem pedida de novo responde sem a base: o link dela some, e o da outra
    // base continua.
    await waitFor(() => expect(listKnowledgeBases).toHaveBeenCalled());
    expect(await screen.findByRole('link', { name: 'Manual do Atendente' })).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('link', { name: BASE_NAME })).not.toBeInTheDocument(),
    );
    // A página não repetiu a consulta da base excluída antes de sair (D3.1).
    expect(getKnowledgeBase).toHaveBeenCalledTimes(1);
  });

  it('409 knowledge-base-active: texto próprio, sem navegar, sem repetir, e a base é relida', async () => {
    vi.mocked(deleteKnowledgeBase).mockRejectedValue(problem(409, 'knowledge-base-active'));
    const user = userEvent.setup();
    const { router } = renderPage();

    const dialog = await openConfirmation(user);
    vi.mocked(getKnowledgeBase).mockResolvedValue(activeBase);
    await user.click(within(dialog).getByRole('button', { name: 'Excluir base' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'A base foi ativada depois que esta tela abriu',
    );
    expect(within(dialog).queryByRole('button', { name: 'Excluir base' })).not.toBeInTheDocument();
    expect(dialog).not.toHaveTextContent(SERVER_TITLE);
    expect(dialog).not.toHaveTextContent(SERVER_DETAIL);
    expect(router.state.location.pathname).toBe(`/knowledge-bases/${BASE_ID}`);
    await waitFor(() => expect(getKnowledgeBase).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: DELETE_BASE })).toHaveAttribute(
        'aria-disabled',
        'true',
      ),
    );
  });

  it('404: já tinha sido excluída, oferece a volta e não navega', async () => {
    vi.mocked(deleteKnowledgeBase).mockRejectedValue(problem(404));
    const user = userEvent.setup();
    const { router } = renderPage();

    const dialog = await openConfirmation(user);
    await user.click(within(dialog).getByRole('button', { name: 'Excluir base' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Esta base já tinha sido excluída',
    );
    expect(within(dialog).getByRole('link', { name: 'Voltar para as bases' })).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Excluir base' })).not.toBeInTheDocument();
    expect(dialog).not.toHaveTextContent(SERVER_TITLE);
    expect(router.state.location.pathname).toBe(`/knowledge-bases/${BASE_ID}`);
    // O detalhe continua montado: a base não foi relida, o que trocaria a página
    // pelo "não encontrada" e desmontaria a confirmação (D3.1).
    expect(getKnowledgeBase).toHaveBeenCalledTimes(1);
  });

  it('503: nada foi excluído, continua oferecendo a exclusão, e não navega', async () => {
    vi.mocked(deleteKnowledgeBase).mockRejectedValueOnce(problem(503));
    const user = userEvent.setup();
    const { router } = renderPage();

    const dialog = await openConfirmation(user);
    await user.click(within(dialog).getByRole('button', { name: 'Excluir base' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Outra operação nesta base impediu a exclusão. Nada foi excluído',
    );
    expect(dialog).not.toHaveTextContent(SERVER_TITLE);
    expect(router.state.location.pathname).toBe(`/knowledge-bases/${BASE_ID}`);

    await user.click(within(dialog).getByRole('button', { name: 'Excluir base' }));
    await waitFor(() => expect(router.state.location.pathname).toBe('/knowledge-bases'));
    expect(deleteKnowledgeBase).toHaveBeenCalledTimes(2);
  });

  it('falha de rede: não afirma o resultado e não navega', async () => {
    vi.mocked(deleteKnowledgeBase).mockRejectedValue(new TypeError('Failed to fetch'));
    const user = userEvent.setup();
    const { router } = renderPage();

    const dialog = await openConfirmation(user);
    await user.click(within(dialog).getByRole('button', { name: 'Excluir base' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Não foi possível confirmar a exclusão',
    );
    expect(router.state.location.pathname).toBe(`/knowledge-bases/${BASE_ID}`);
  });
});

// Cenário "O alerta de base inativa também não manda excluir" de
// knowledge-base-sync-state-ui: com o botão de exclusão disponível no cabeçalho, o
// alerta de falha continua sem a orientação (D6).
describe('alerta de falha em base sincronizada inativa', () => {
  it('o card de Origem não contém exclu, remov nem apag, e o botão de excluir está disponível', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue({
      ...inactiveBase,
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
    renderPage();

    const card = await screen.findByTestId('sync-origin-card');
    expect(within(card).getByTestId('sync-failure-alert')).toHaveTextContent('Sem acesso à pasta');
    expect(card).not.toHaveTextContent(/exclu|remov|apag/i);
    expect(await deleteButton()).not.toHaveAttribute('aria-disabled');
  });
});
