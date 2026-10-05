import { act } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseDetailPage } from './KnowledgeBaseDetailPage';
import { SYNC_WAIT_LIMIT_MS } from '../utils/syncState';
import type { KnowledgeBase, KnowledgeBaseSyncState } from '../types/knowledgeBase';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';

// ACEITES DA #107 (frontend-detalhe-base-sincronizada, D10). SEM mock de módulo,
// ao contrário de KnowledgeBaseDetailPage.test.tsx: o `fetch` global é
// interceptado, e as negativas de rede ("nenhuma chamada ao apps/connectors")
// afirmam sobre TUDO o que saiu. Cada uma confere antes que o interceptor viu as
// chamadas ao apps/api, para a ausência não passar por vacuidade.

// O cliente do apps/api cai no default de dev quando VITE_API_BASE_URL não existe,
// que é o caso da suíte (knowledgeBasesApi.ts).
const API = 'http://localhost:5017';
const CONNECTORS = 'http://conectores.test';
const ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const EMAIL = 'sync@exemplo.iam.gserviceaccount.com';
const T0 = '2026-10-04T10:00:00Z';
const T1 = '2026-10-04T10:05:00Z';
const T2 = '2026-10-04T10:05:40Z';

const NEVER: KnowledgeBaseSyncState = {
  lastCompletedAt: null,
  lastFinishedAt: null,
  failingSince: null,
  lastError: null,
  ignoredFiles: null,
};

function synced(state: Partial<KnowledgeBaseSyncState> = {}): KnowledgeBase {
  return {
    id: ID,
    name: 'FAQ de Suporte',
    description: 'Dúvidas frequentes de clientes sobre conta, entrega e trocas.',
    isActive: true,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    contentMode: 'Synced',
    syncSource: {
      provider: 'google-drive',
      folderId: 'f-faq',
      folderName: 'FAQ Suporte',
      folderUrl: 'https://drive.google.com/drive/folders/f-faq',
    },
    syncState: { ...NEVER, ...state },
  };
}

function upToDate(at: string): KnowledgeBase {
  return synced({ lastCompletedAt: at, lastFinishedAt: at, ignoredFiles: [] });
}

const manual: KnowledgeBase = {
  ...synced(),
  contentMode: 'Manual',
  syncSource: null,
  syncState: null,
};

function documento(overrides: Partial<KnowledgeDocumentSummary> = {}): KnowledgeDocumentSummary {
  return {
    id: 'doc-1',
    knowledgeBaseId: ID,
    title: 'Como redefinir a senha',
    sourceType: 'markdown',
    contentLengthBytes: 840,
    indexingStatus: 'Indexed',
    indexedAt: '2026-10-02T03:14:00Z',
    failureReason: null,
    contentRevision: 1,
    fragmentCount: 6,
    indexingAttempts: 1,
    lastAttemptAt: '2026-10-02T03:14:00Z',
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-02T00:00:00Z',
    ...overrides,
  };
}

type Reply = Response | 'network';

interface Call {
  url: string;
  method: string;
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

// `base` é chamada a cada GET da base, para a sequência de respostas mudar entre
// consultas; `sync` responde o POST do apps/connectors.
function installFetch(options: {
  base: () => KnowledgeBase;
  sync?: () => Reply;
  documents?: KnowledgeDocumentSummary[];
}) {
  const calls: Call[] = [];
  const fetchMock = vi.fn((input: string, init?: RequestInit) => {
    const url = new URL(input, 'http://painel.test');
    const method = init?.method ?? 'GET';
    calls.push({ url: url.toString(), method });
    const path = url.pathname;
    let reply: Reply;
    if (url.origin === CONNECTORS && path === `/connectors/knowledge-bases/${ID}/sync`) {
      reply = options.sync ? options.sync() : new Response(null, { status: 202 });
    } else if (url.origin === API && path === `/knowledge-bases/${ID}`) {
      reply = json(options.base());
    } else if (url.origin === API && path === `/knowledge-bases/${ID}/documents`) {
      reply = json(options.documents ?? [documento()]);
    } else if (url.origin === API && path === `/knowledge-bases/${ID}/documents/doc-f/reindex`) {
      reply = json(documento({ id: 'doc-f', indexingStatus: 'Pending', indexedAt: null }));
    } else if (url.origin === API && path === '/agents') {
      reply = json([]);
    } else {
      reply = json({ title: 'rota não montada no teste' }, 599);
    }
    if (reply === 'network') return Promise.reject(new TypeError('Failed to fetch'));
    return Promise.resolve(reply);
  });
  vi.stubGlobal('fetch', fetchMock);
  return calls;
}

const connectorCalls = (calls: Call[]) =>
  calls.filter((call) => call.url.startsWith(CONNECTORS) || call.url.includes('/connectors/'));
const baseGets = (calls: Call[]) =>
  calls.filter((call) => call.method === 'GET' && call.url === `${API}/knowledge-bases/${ID}`);
const documentGets = (calls: Call[]) =>
  calls.filter(
    (call) => call.method === 'GET' && call.url === `${API}/knowledge-bases/${ID}/documents`,
  );

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/knowledge-bases/:id', element: <KnowledgeBaseDetailPage /> },
      { path: '/knowledge-bases', element: <p>listagem de bases</p> },
    ],
    { initialEntries: [`/knowledge-bases/${ID}`] },
  );
  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <Notifications />
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );
}

async function loaded() {
  await screen.findByText('Como redefinir a senha');
  return screen.getByTestId('sync-origin-card');
}

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

describe('base sincronizada: documentos somente leitura', () => {
  it('não oferece adicionar, atualizar nem excluir, e explica de onde vêm os documentos', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    installFetch({ base: () => upToDate(T0) });
    renderPage();
    await loaded();

    expect(screen.queryByRole('button', { name: 'Adicionar documento' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Atualizar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Excluir' })).not.toBeInTheDocument();
    expect(screen.getByText('Somente leitura — o conteúdo vem da pasta')).toBeInTheDocument();
    expect(screen.getByTestId('ignored-files-card')).toBeInTheDocument();
  });

  it('reindexar continua, e chama a rota de reindexação', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    const calls = installFetch({
      base: () => upToDate(T0),
      documents: [
        documento({
          id: 'doc-f',
          title: 'Garantia estendida',
          indexingStatus: 'Failed',
          indexedAt: null,
          failureReason: 'x',
        }),
        documento(),
      ],
    });
    renderPage();
    await loaded();

    await user.click(screen.getByRole('button', { name: 'Reindexar documento' }));

    await waitFor(() =>
      expect(
        calls.some(
          (call) => call.method === 'POST' && call.url.endsWith('/documents/doc-f/reindex'),
        ),
      ).toBe(true),
    );
  });
});

describe('estado da sincronização', () => {
  it('nunca sincronizou não aparece como falha, e a lista nula não é "nenhum arquivo"', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    installFetch({ base: () => synced() });
    renderPage();
    const card = await loaded();

    expect(card).not.toHaveTextContent(/falha/i);
    expect(screen.queryByTestId('sync-failure-alert')).not.toBeInTheDocument();
    const ignored = screen.getByTestId('ignored-files-card');
    expect(ignored).toHaveTextContent(
      'A lista aparece depois da primeira sincronização concluída.',
    );
    expect(ignored).not.toHaveTextContent('Nenhum arquivo');
  });

  it('access-denied mostra o e-mail da conta', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    installFetch({
      base: () =>
        synced({
          lastCompletedAt: T0,
          lastFinishedAt: T1,
          failingSince: T1,
          lastError: { code: 'access-denied', detail: EMAIL },
          ignoredFiles: [],
        }),
    });
    renderPage();
    await loaded();

    expect(screen.getByTestId('sync-failure-alert')).toHaveTextContent(EMAIL);
  });

  it('rate-limited é passageira e não fala de acesso', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    installFetch({
      base: () =>
        synced({
          lastCompletedAt: T0,
          lastFinishedAt: T1,
          failingSince: T1,
          lastError: { code: 'rate-limited', detail: null },
          ignoredFiles: [],
        }),
    });
    renderPage();
    await loaded();

    const alert = screen.getByTestId('sync-failure-alert');
    expect(alert).toHaveTextContent('A falha é passageira');
    expect(alert).not.toHaveTextContent(/acesso|compartilh/i);
  });
});

describe('Sincronizar agora', () => {
  it('202 sem mudança: solicitada, botão desabilitado, e nenhum texto de conclusão', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    const calls = installFetch({ base: () => upToDate(T0) });
    renderPage();
    const card = await loaded();

    await user.click(within(card).getByRole('button', { name: 'Sincronizar agora' }));

    expect(await within(card).findByTestId('sync-request-status')).toHaveTextContent(
      'Sincronização solicitada. Aguardando o resultado…',
    );
    expect(within(card).getByRole('button', { name: 'Sincronizar agora' })).toBeDisabled();
    expect(card).not.toHaveTextContent(/concluída em|terminou/);
    expect(connectorCalls(calls)).toEqual([
      { url: `${CONNECTORS}/connectors/knowledge-bases/${ID}/sync`, method: 'POST' },
    ]);
  });

  it('a troca de lastFinishedAt mostra a conclusão e refaz a listagem de documentos', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    let current = upToDate(T0);
    const calls = installFetch({ base: () => current });
    renderPage();
    const card = await loaded();

    await user.click(within(card).getByRole('button', { name: 'Sincronizar agora' }));
    await within(card).findByText('Sincronização solicitada. Aguardando o resultado…');
    const documentsBefore = documentGets(calls).length;

    current = upToDate(T2);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4500);
    });

    expect(await within(card).findByTestId('sync-request-status')).toHaveTextContent(
      'Sincronização concluída em',
    );
    expect(within(card).getByRole('button', { name: 'Sincronizar agora' })).toBeEnabled();
    await waitFor(() => expect(documentGets(calls).length).toBeGreaterThan(documentsBefore));

    const basesAfter = baseGets(calls).length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(20000);
    });
    expect(baseGets(calls).length).toBe(basesAfter);
  });

  it('a linha de base vem da releitura, e não do carregamento da página', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    // Uma rodada periódica terminou depois de a página carregar: a releitura do
    // clique já traz T1, e as consultas seguintes continuam em T1.
    let current = upToDate(T0);
    installFetch({ base: () => current });
    renderPage();
    const card = await loaded();
    current = upToDate(T1);

    await user.click(within(card).getByRole('button', { name: 'Sincronizar agora' }));
    await within(card).findByText('Sincronização solicitada. Aguardando o resultado…');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(9000);
    });

    expect(within(card).getByTestId('sync-request-status')).toHaveTextContent(
      'Sincronização solicitada. Aguardando o resultado…',
    );
    expect(card).not.toHaveTextContent('Sincronização concluída em');
  });

  it('sem resultado em 5 minutos: para de consultar e não afirma sucesso nem falha', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const calls = installFetch({ base: () => upToDate(T0) });
    renderPage();
    const card = await loaded();

    await user.click(within(card).getByRole('button', { name: 'Sincronizar agora' }));
    await within(card).findByText('Sincronização solicitada. Aguardando o resultado…');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(SYNC_WAIT_LIMIT_MS + 1000);
    });

    const status = within(card).getByTestId('sync-request-status');
    expect(status).toHaveTextContent('Nenhum resultado foi gravado em 5 minutos.');
    expect(status).not.toHaveTextContent(/concluída em|terminou com falha/);
    expect(within(card).getByRole('button', { name: 'Sincronizar agora' })).toBeEnabled();

    const basesAtLimit = baseGets(calls).length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(60000);
    });
    expect(baseGets(calls).length).toBe(basesAtLimit);
  });

  it('apps/connectors fora do ar: diz que pode estar fora do ar, e não começa a acompanhar', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const calls = installFetch({ base: () => upToDate(T0), sync: () => 'network' });
    renderPage();
    const card = await loaded();

    await user.click(within(card).getByRole('button', { name: 'Sincronizar agora' }));

    expect(await within(card).findByTestId('sync-request-status')).toHaveTextContent(
      'Ele pode estar fora do ar',
    );
    expect(within(card).getByRole('button', { name: 'Sincronizar agora' })).toBeEnabled();

    const basesAfterError = baseGets(calls).length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(20000);
    });
    expect(baseGets(calls).length).toBe(basesAfterError);
  });
});

describe('sem VITE_CONNECTORS_BASE_URL', () => {
  it('botão indisponível com explicação, estado visível, e nenhuma chamada ao apps/connectors', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', undefined);
    const user = userEvent.setup();
    const calls = installFetch({
      base: () =>
        synced({
          lastCompletedAt: T0,
          lastFinishedAt: T1,
          failingSince: T1,
          lastError: { code: 'access-denied', detail: EMAIL },
          ignoredFiles: [],
        }),
    });
    renderPage();
    const card = await loaded();

    const button = within(card).getByRole('button', { name: 'Tentar sincronizar agora' });
    expect(button).toBeDisabled();
    await user.click(button).catch(() => undefined);
    expect(within(card).getByTestId('sync-unavailable')).toHaveTextContent(
      'A sincronização manual não está habilitada neste painel',
    );
    expect(screen.getByTestId('sync-failure-alert')).toHaveTextContent(EMAIL);

    // O interceptor viu o apps/api — a negativa abaixo não passa por vacuidade.
    expect(baseGets(calls).length).toBeGreaterThan(0);
    expect(documentGets(calls).length).toBeGreaterThan(0);
    expect(connectorCalls(calls)).toEqual([]);
  });
});

describe('base manual', () => {
  it('nada da sincronização aparece, e nenhuma chamada ao apps/connectors', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const calls = installFetch({ base: () => manual });
    renderPage();
    await screen.findByText('Como redefinir a senha');

    expect(screen.queryByTestId('sync-origin-card')).not.toBeInTheDocument();
    expect(screen.queryByTestId('ignored-files-card')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Adicionar documento' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Atualizar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Excluir' })).toBeInTheDocument();

    expect(baseGets(calls).length).toBeGreaterThan(0);
    expect(connectorCalls(calls)).toEqual([]);
  });
});
