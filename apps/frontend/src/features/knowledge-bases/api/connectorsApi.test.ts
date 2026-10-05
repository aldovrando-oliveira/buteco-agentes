import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ConnectorsError,
  connectorsBaseUrl,
  listConnectorFolders,
  listConnectorProviders,
  requestKnowledgeBaseSync,
} from './connectorsApi';
import { clearToken, getToken, setToken } from '../../../auth/token';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

function stubFetch(body: unknown, status = 200) {
  const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body, status));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function calledUrl(fetchMock: ReturnType<typeof vi.fn>): string {
  return fetchMock.mock.calls[0][0] as string;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
  clearToken();
});

describe('connectorsBaseUrl', () => {
  it('é nulo quando a variável não foi definida no build', () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', undefined);
    expect(connectorsBaseUrl()).toBeNull();
  });

  it('é string vazia quando a variável veio vazia (caminho relativo)', () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', '');
    expect(connectorsBaseUrl()).toBe('');
  });

  it('é o endereço configurado quando há valor', () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    expect(connectorsBaseUrl()).toBe('http://localhost:5037');
  });
});

describe('listConnectorProviders', () => {
  it('chama /connectors/providers no endereço configurado, com o token do operador', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    setToken('token-do-operador');
    const fetchMock = stubFetch([
      { key: 'google-drive', accountEmail: 'a@b.iam.gserviceaccount.com' },
    ]);

    const providers = await listConnectorProviders();

    expect(calledUrl(fetchMock)).toBe('http://localhost:5037/connectors/providers');
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer token-do-operador');
    expect(providers).toEqual([
      { key: 'google-drive', accountEmail: 'a@b.iam.gserviceaccount.com' },
    ]);
  });

  it('com a variável vazia, chama o caminho relativo', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', '');
    const fetchMock = stubFetch([]);

    await listConnectorProviders();

    expect(calledUrl(fetchMock)).toBe('/connectors/providers');
  });

  it('sem a variável, recusa sem fazer nenhuma requisição', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', undefined);
    const fetchMock = stubFetch([]);

    await expect(listConnectorProviders()).rejects.toThrow();

    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('listConnectorFolders', () => {
  it('sem parentId, chama a rota sem query string', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    const fetchMock = stubFetch([]);

    await listConnectorFolders('google-drive');

    expect(calledUrl(fetchMock)).toBe(
      'http://localhost:5037/connectors/providers/google-drive/folders',
    );
  });

  it('com parentId, escapa o id e o provedor', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    const fetchMock = stubFetch([]);

    await listConnectorFolders('google drive', 'a/b c');

    expect(calledUrl(fetchMock)).toBe(
      'http://localhost:5037/connectors/providers/google%20drive/folders?parentId=a%2Fb+c',
    );
  });
});

describe('erros', () => {
  it('falha de rede vira ConnectorsError com kind "network"', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = await listConnectorProviders().catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ConnectorsError);
    expect((error as ConnectorsError).kind).toBe('network');
    expect((error as ConnectorsError).status).toBeUndefined();
  });

  it('ProblemDetails com code e detail chegam no erro', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    stubFetch(
      {
        title: 'Sem acesso',
        status: 422,
        code: 'access-denied',
        detail: 'conta@x.iam.gserviceaccount.com',
      },
      422,
    );

    const error = (await listConnectorFolders('google-drive', 'p').catch(
      (e: unknown) => e,
    )) as ConnectorsError;

    expect(error).toBeInstanceOf(ConnectorsError);
    expect(error.kind).toBe('http');
    expect(error.status).toBe(422);
    expect(error.code).toBe('access-denied');
    expect(error.detail).toBe('conta@x.iam.gserviceaccount.com');
  });

  it('erro sem corpo JSON chega só com o status', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('oops', { status: 500 })));

    const error = (await listConnectorProviders().catch((e: unknown) => e)) as ConnectorsError;

    expect(error.status).toBe(500);
    expect(error.code).toBeUndefined();
  });

  it('401 NÃO limpa o token nem redireciona para /login', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    setToken('token-valido-no-apps-api');
    stubFetch({ title: 'unauthorized' }, 401);
    const originalLocation = window.location;
    Object.defineProperty(window, 'location', {
      value: { ...originalLocation, href: 'http://localhost/knowledge-bases/new' },
      writable: true,
      configurable: true,
    });

    try {
      const error = (await listConnectorProviders().catch((e: unknown) => e)) as ConnectorsError;

      expect(error).toBeInstanceOf(ConnectorsError);
      expect(error.status).toBe(401);
      expect(getToken()).toBe('token-valido-no-apps-api');
      expect(window.location.href).toBe('http://localhost/knowledge-bases/new');
    } finally {
      Object.defineProperty(window, 'location', {
        value: originalLocation,
        writable: true,
        configurable: true,
      });
    }
  });
});

describe('requestKnowledgeBaseSync', () => {
  const ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

  it('faz POST em /connectors/knowledge-bases/{id}/sync e resolve no 202 sem corpo', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    setToken('token-do-operador');
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(requestKnowledgeBaseSync(ID)).resolves.toBeUndefined();

    expect(calledUrl(fetchMock)).toBe(
      `http://localhost:5037/connectors/knowledge-bases/${ID}/sync`,
    );
    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe('POST');
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer token-do-operador');
  });

  it('escapa o id no caminho', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', '');
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }));
    vi.stubGlobal('fetch', fetchMock);

    await requestKnowledgeBaseSync('a/b');

    expect(calledUrl(fetchMock)).toBe('/connectors/knowledge-bases/a%2Fb/sync');
  });

  it('erro com code chega no ConnectorsError', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    stubFetch({ status: 503, code: 'sync-not-configured' }, 503);

    const error = await requestKnowledgeBaseSync(ID).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ConnectorsError);
    expect((error as ConnectorsError).kind).toBe('http');
    expect((error as ConnectorsError).status).toBe(503);
    expect((error as ConnectorsError).code).toBe('sync-not-configured');
  });

  it('falha de rede vira kind "network"', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = await requestKnowledgeBaseSync(ID).catch((caught: unknown) => caught);

    expect((error as ConnectorsError).kind).toBe('network');
  });

  it('sem a variável, recusa sem fazer nenhuma requisição', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', undefined);
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    await expect(requestKnowledgeBaseSync(ID)).rejects.toBeInstanceOf(ConnectorsError);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
