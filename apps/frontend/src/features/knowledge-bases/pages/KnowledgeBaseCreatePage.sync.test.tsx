import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseCreatePage } from './KnowledgeBaseCreatePage';
import { clearToken, getToken, setToken } from '../../../auth/token';
import { ApiError } from '../api/knowledgeBasesApi';
import { ConnectorsError } from '../api/connectorsApi';
import { connectorErrorMessage } from '../utils/connectorErrors';
import type { KnowledgeBase } from '../types/knowledgeBase';
import type { ConnectorFolder } from '../types/connectors';

// SEM mock de módulo, ao contrário de KnowledgeBaseCreatePage.test.tsx: aqui o
// `fetch` global é interceptado, e as negativas de rede ("nenhuma chamada ao
// apps/connectors") afirmam sobre TUDO o que saiu. Cada uma confere antes que o
// interceptor viu as chamadas ao apps/api, para a ausência não passar por
// vacuidade.

// O cliente do apps/api cai no default de dev quando VITE_API_BASE_URL não existe,
// que é o caso da suíte (knowledgeBasesApi.ts).
const API = 'http://localhost:5017';
const CONNECTORS = 'http://conectores.test';
const EMAIL = 'buteco-sync@projeto.iam.gserviceaccount.com';

const NAME = 'FAQ de Suporte';
const DESCRIPTION =
  'Dúvidas frequentes de clientes sobre conta, entrega e trocas. Consulte antes de responder sobre prazos.';

function folder(
  id: string,
  name: string,
  kind: ConnectorFolder['kind'] = 'Folder',
): ConnectorFolder {
  return { id, name, kind, webUrl: `https://drive.google.com/drive/folders/${id}` };
}
const suporte = folder('d-suporte', 'Suporte', 'SharedDrive');
const guia = folder('f-guia', 'Guia do produto');
const faq = folder('f-faq', 'FAQ Suporte');
const rh = folder('f-rh', 'Políticas RH');

function kb(overrides: Partial<KnowledgeBase>): KnowledgeBase {
  return {
    id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    name: NAME,
    description: DESCRIPTION,
    isActive: true,
    createdAt: '2026-10-03T00:00:00Z',
    updatedAt: '2026-10-03T00:00:00Z',
    contentMode: 'Manual',
    syncSource: null,
    syncState: null,
    ...overrides,
  };
}

const rhBase = kb({
  id: 'b-rh',
  name: 'Políticas internas de RH',
  isActive: false,
  contentMode: 'Synced',
  syncSource: {
    provider: 'google-drive',
    folderId: 'f-rh',
    folderName: 'Políticas RH',
    folderUrl: 'https://x',
  },
  syncState: {
    lastCompletedAt: null,
    lastFinishedAt: null,
    failingSince: null,
    lastError: null,
    ignoredFiles: null,
  },
});

type Reply = Response | 'network' | 'pending';
type Handler = (url: URL, init: RequestInit | undefined) => Reply | undefined;

interface Call {
  url: string;
  method: string;
  body: unknown;
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function problem(status: number, extra: Record<string, unknown>) {
  return json({ title: 'TITULO-DO-SERVIDOR', status, ...extra }, status);
}

// Respostas padrão de um caminho feliz; `overrides` vem antes e vence.
function installFetch(overrides: Handler = () => undefined) {
  const calls: Call[] = [];
  const fetchMock = vi.fn((input: string, init?: RequestInit) => {
    const url = new URL(input, 'http://painel.test');
    calls.push({
      url: url.toString(),
      method: init?.method ?? 'GET',
      body: init?.body ? JSON.parse(init.body as string) : undefined,
    });
    const reply =
      overrides(url, init) ??
      (() => {
        const path = url.pathname;
        if (url.origin === CONNECTORS && path === '/connectors/providers') {
          return json([{ key: 'google-drive', accountEmail: EMAIL }]);
        }
        if (url.origin === CONNECTORS && path === '/connectors/providers/google-drive/folders') {
          const parent = url.searchParams.get('parentId');
          if (parent === null) return json([suporte, guia]);
          if (parent === 'd-suporte') return json([faq, rh]);
          return json([]);
        }
        if (
          url.origin === API &&
          path === '/knowledge-bases' &&
          (init?.method ?? 'GET') === 'GET'
        ) {
          return json([rhBase]);
        }
        if (url.origin === API && path === '/knowledge-bases' && init?.method === 'POST') {
          const body = JSON.parse(init.body as string) as Record<string, unknown>;
          return json(
            kb({
              contentMode: body.contentMode === 'Synced' ? 'Synced' : 'Manual',
            }),
            201,
          );
        }
        return json({ title: 'rota não montada no teste' }, 599);
      })();
    if (reply === 'network') return Promise.reject(new TypeError('Failed to fetch'));
    if (reply === 'pending') return new Promise<Response>(() => {});
    return Promise.resolve(reply);
  });
  vi.stubGlobal('fetch', fetchMock);
  return calls;
}

function connectorCalls(calls: Call[]) {
  return calls.filter(
    (call) => call.url.startsWith(CONNECTORS) || call.url.includes('/connectors/'),
  );
}

function apiCalls(calls: Call[]) {
  return calls.filter((call) => call.url.startsWith(API));
}

function renderPage() {
  const queryClient = new QueryClient();
  const router = createMemoryRouter(
    [
      { path: '/knowledge-bases/new', element: <KnowledgeBaseCreatePage /> },
      { path: '/knowledge-bases', element: <div>Catálogo de bases</div> },
      { path: '/knowledge-bases/:id', element: <div>Detalhe da base</div> },
    ],
    { initialEntries: ['/knowledge-bases/new'] },
  );
  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <Notifications />
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );
  return router;
}

async function fill(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/Nome/), NAME);
  await user.type(screen.getByLabelText(/Descrição/), DESCRIPTION);
}

function originCard() {
  return screen.getByTestId('origin-card');
}

async function chooseSynced(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('radio', { name: /Sincronizada/ }));
  await screen.findByText(EMAIL);
}

async function pickFaq(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Escolher pasta' }));
  const dialog = await screen.findByRole('dialog', { name: 'Escolher pasta' });
  await user.click(await within(dialog).findByRole('button', { name: 'Abrir Suporte' }));
  await user.click(await within(dialog).findByRole('radio', { name: 'Selecionar FAQ Suporte' }));
  await user.click(within(dialog).getByRole('button', { name: 'Selecionar “FAQ Suporte”' }));
  await waitFor(() =>
    expect(screen.queryByRole('dialog', { name: 'Escolher pasta' })).not.toBeInTheDocument(),
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
  clearToken();
});

describe('sem VITE_CONNECTORS_BASE_URL', () => {
  it('oferece só Manual, com a explicação, e nenhuma chamada sai para o apps/connectors', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', undefined);
    const user = userEvent.setup();
    const calls = installFetch();

    const router = renderPage();
    const synced = screen.getByRole('radio', { name: /Sincronizada/ });
    expect(synced).toBeDisabled();
    expect(originCard()).toHaveTextContent(
      'A sincronização com pastas não está habilitada neste painel.',
    );
    await user.click(synced);
    await fill(user);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));
    await screen.findByText('Detalhe da base');

    expect(router.state.location.pathname).toBe(
      '/knowledge-bases/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    );
    // Não vácuo: o interceptor viu o POST ao apps/api.
    expect(apiCalls(calls).map((call) => call.method)).toContain('POST');
    expect(connectorCalls(calls)).toEqual([]);
    expect(calls.some((call) => call.url.includes('5037'))).toBe(false);
  });
});

describe('com VITE_CONNECTORS_BASE_URL', () => {
  it('com Manual marcada, cria com exatamente { name, description } e não chama o apps/connectors', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    const calls = installFetch();

    renderPage();
    await fill(user);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));
    await screen.findByText('Detalhe da base');

    const post = apiCalls(calls).find((call) => call.method === 'POST');
    expect(post).toBeDefined();
    expect(post!.body).toEqual({ name: NAME, description: DESCRIPTION });
    expect(Object.keys(post!.body as object).sort()).toEqual(['description', 'name']);
    expect(connectorCalls(calls)).toEqual([]);
  });

  it('Sincronizada lista o provedor, mostra o e-mail, navega o seletor e cria com as cinco chaves', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    const calls = installFetch();

    const router = renderPage();
    await fill(user);
    await chooseSynced(user);
    expect(screen.getByRole('combobox', { name: /Provedor/ })).toHaveValue('Google Drive');
    expect(originCard()).toHaveTextContent(/como Leitor/);

    await pickFaq(user);
    expect(originCard()).toHaveTextContent('FAQ Suporte');
    expect(screen.getByRole('link', { name: /Abrir no Drive/ })).toHaveAttribute(
      'href',
      faq.webUrl,
    );

    await user.click(screen.getByRole('button', { name: 'Criar base' }));
    await screen.findByText('Detalhe da base');
    expect(router.state.location.pathname).toBe(
      '/knowledge-bases/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    );

    const folderCalls = connectorCalls(calls)
      .filter((call) => call.url.includes('/folders'))
      .map((call) => call.url);
    expect(folderCalls).toEqual([
      `${CONNECTORS}/connectors/providers/google-drive/folders`,
      `${CONNECTORS}/connectors/providers/google-drive/folders?parentId=d-suporte`,
    ]);
    const post = apiCalls(calls).find((call) => call.method === 'POST');
    expect(post!.body).toEqual({
      name: NAME,
      description: DESCRIPTION,
      contentMode: 'Synced',
      provider: 'google-drive',
      folderId: 'f-faq',
    });
  });

  it('Sincronizada sem pasta é barrada no cliente, sem POST', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    const calls = installFetch();

    renderPage();
    await fill(user);
    await chooseSynced(user);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(
      await screen.findByText('Escolha a pasta que a base vai acompanhar.'),
    ).toBeInTheDocument();
    expect(calls.some((call) => call.method === 'POST')).toBe(false);
  });

  it('a nota de rodapé muda com a origem, e a sincronizada não promete prazo', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    installFetch();

    renderPage();
    const note = () => screen.getByTestId('documents-note');
    expect(note()).toHaveTextContent('Documentos são carregados depois de criar a base');
    await chooseSynced(user);

    expect(note()).toHaveTextContent(/sincronização com a pasta/);
    expect(note()).not.toHaveTextContent('Documentos são carregados depois de criar a base');
    expect(note().textContent).not.toMatch(/minuto|imediatamente|\bagora\b/i);
  });

  it('pasta em uso aparece desabilitada no seletor, com o nome da base', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    installFetch();

    renderPage();
    await chooseSynced(user);
    await user.click(screen.getByRole('button', { name: 'Escolher pasta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Escolher pasta' });
    await user.click(await within(dialog).findByRole('button', { name: 'Abrir Suporte' }));

    const row = await within(dialog).findByTestId('folder-row-Políticas RH');
    await waitFor(() => expect(within(row).getByRole('radio')).toBeDisabled());
    expect(row).toHaveTextContent('Já sincronizada pela base “Políticas internas de RH”');
  });

  it('listagem de bases indisponível: o seletor avisa e não desabilita nenhuma pasta', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    installFetch((url, init) =>
      url.origin === API && url.pathname === '/knowledge-bases' && (init?.method ?? 'GET') === 'GET'
        ? json({ title: 'x' }, 500)
        : undefined,
    );

    renderPage();
    await chooseSynced(user);
    await user.click(screen.getByRole('button', { name: 'Escolher pasta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Escolher pasta' });
    await user.click(await within(dialog).findByRole('button', { name: 'Abrir Suporte' }));

    // A listagem de bases usa o hook da tela de listagem, com as novas tentativas
    // padrão do React Query (três, com espera crescente): o aviso chega depois
    // delas, e até lá o seletor não marca nem avisa nada (design.md, D4).
    expect(
      await within(dialog).findByText(
        /Não foi possível conferir quais pastas já são usadas/,
        {},
        {
          timeout: 12000,
        },
      ),
    ).toBeInTheDocument();
    expect(
      within(within(dialog).getByTestId('folder-row-Políticas RH')).getByRole('radio'),
    ).toBeEnabled();
  }, 20000);
});

describe('carregamento sem texto de erro', () => {
  const ERROR_WORDS = /Não foi possível|erro|fora do ar|sem acesso/i;

  it('provedores carregando', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    installFetch((url) => (url.pathname === '/connectors/providers' ? 'pending' : undefined));

    renderPage();
    await user.click(screen.getByRole('radio', { name: /Sincronizada/ }));

    expect(await within(originCard()).findByText('Carregando provedores…')).toBeInTheDocument();
    expect(originCard().textContent).not.toMatch(ERROR_WORDS);
  });

  it('seletor carregando', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    installFetch((url) => (url.pathname.endsWith('/folders') ? 'pending' : undefined));

    renderPage();
    await chooseSynced(user);
    await user.click(screen.getByRole('button', { name: 'Escolher pasta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Escolher pasta' });

    expect(await within(dialog).findByText('Carregando pastas…')).toBeInTheDocument();
    expect(dialog.textContent).not.toMatch(
      /Não foi possível|erro|fora do ar|sem acesso|Nenhuma|Nada foi/i,
    );
  });
});

describe('texto de cada código', () => {
  const navigationCodes: Array<[string, number, string | undefined]> = [
    ['provider-not-configured', 404, undefined],
    ['access-denied', 422, EMAIL],
    ['not-a-folder', 422, undefined],
    ['folder-trashed', 422, undefined],
    ['api-not-configured', 502, undefined],
    ['provider-auth-failed', 502, undefined],
    ['provider-error', 502, 'bad-request'],
    ['rate-limited', 503, undefined],
    ['provider-unavailable', 503, undefined],
  ];

  it.each(navigationCodes)('navegação: %s no seletor', async (code, status, detail) => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    installFetch((url) =>
      url.searchParams.get('parentId') === 'd-suporte'
        ? problem(status, { code, detail })
        : undefined,
    );

    renderPage();
    await chooseSynced(user);
    await user.click(screen.getByRole('button', { name: 'Escolher pasta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Escolher pasta' });
    await user.click(await within(dialog).findByRole('button', { name: 'Abrir Suporte' }));

    const expected = connectorErrorMessage(
      new ConnectorsError('http', 'x', { status, code, detail }),
      'navigation',
      'google-drive',
    );
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(expected);
    expect(dialog).not.toHaveTextContent('TITULO-DO-SERVIDOR');
    expect(dialog).not.toHaveTextContent('Esta pasta não tem subpastas.');
  });

  it('navegação: apps/connectors fora do ar ao listar provedores', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    installFetch((url) => (url.pathname === '/connectors/providers' ? 'network' : undefined));

    renderPage();
    await user.click(screen.getByRole('radio', { name: /Sincronizada/ }));

    expect(
      await within(originCard()).findByText(/Não foi possível falar com o serviço de conectores/),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tentar de novo' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: /Provedor/ })).not.toBeInTheDocument();
  });

  it('navegação: apps/connectors fora do ar no seletor', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    installFetch((url) => (url.pathname.endsWith('/folders') ? 'network' : undefined));

    renderPage();
    await chooseSynced(user);
    await user.click(screen.getByRole('button', { name: 'Escolher pasta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Escolher pasta' });

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      /Não foi possível falar com o serviço de conectores/,
    );
  });

  it('navegação: 401 do apps/connectors não desloga nem navega para /login', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    setToken('token-do-operador');
    const user = userEvent.setup();
    const calls = installFetch((url) =>
      url.pathname.endsWith('/folders') ? json({ title: 'unauthorized' }, 401) : undefined,
    );

    const router = renderPage();
    await chooseSynced(user);
    await user.click(screen.getByRole('button', { name: 'Escolher pasta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Escolher pasta' });

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(/não aceitou a sua sessão/);
    expect(calls.some((call) => call.url.endsWith('/folders'))).toBe(true);
    expect(getToken()).toBe('token-do-operador');
    expect(router.state.location.pathname).toBe('/knowledge-bases/new');
  });

  const createCodes: Array<[string, number, Record<string, unknown>]> = [
    [
      'folder-in-use',
      409,
      {
        knowledgeBaseId: 'b-rh',
        knowledgeBaseName: 'Políticas internas de RH',
        detail: 'A pasta já é usada pela base Políticas internas de RH.',
      },
    ],
    ['connectors-not-configured', 503, {}],
    ['connectors-unavailable', 503, {}],
    ['connectors-error', 502, { detail: '401' }],
    ['provider-not-configured', 422, {}],
    ['access-denied', 422, { detail: EMAIL }],
    ['not-a-folder', 422, {}],
    ['folder-trashed', 422, {}],
    ['api-not-configured', 502, {}],
    ['provider-auth-failed', 502, {}],
    ['provider-error', 502, {}],
    ['rate-limited', 503, {}],
    ['provider-unavailable', 503, {}],
    ['folder-too-deep', 422, {}],
  ];

  it.each(createCodes)('cadastro: %s no card de Origem', async (code, status, extra) => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', CONNECTORS);
    const user = userEvent.setup();
    installFetch((url, init) =>
      url.origin === API && init?.method === 'POST'
        ? problem(status, { code, ...extra })
        : undefined,
    );

    const router = renderPage();
    await fill(user);
    await chooseSynced(user);
    await pickFaq(user);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    const expected = connectorErrorMessage(
      new ApiError(status, 'x', { status, code, ...extra }),
      'create',
      'google-drive',
    );
    const alert = await within(originCard()).findByRole('alert');
    expect(alert).toHaveTextContent(expected);
    expect(alert).toHaveTextContent('Nenhuma base foi criada.');
    expect(originCard()).not.toHaveTextContent('TITULO-DO-SERVIDOR');
    expect(router.state.location.pathname).toBe('/knowledge-bases/new');
    expect(screen.getByLabelText(/Nome/)).toHaveValue(NAME);
    expect(screen.getByLabelText(/Descrição/)).toHaveValue(DESCRIPTION);
    if (code === 'folder-in-use') {
      expect(alert).toHaveTextContent('Políticas internas de RH');
      expect(alert.textContent).not.toMatch(/exclu|remov|apag/i);
    }
  });
});
