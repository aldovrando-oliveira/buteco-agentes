import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, getMessagesSummary, getSessionsSummary } from './sessionsApi';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const from = '2026-09-11T14:30:15.250Z';
const to = '2026-09-18T14:30:15.250Z';

afterEach(() => {
  vi.unstubAllGlobals();
});

function calledUrl(fetchMock: ReturnType<typeof vi.fn>): URL {
  return new URL(fetchMock.mock.calls[0][0] as string);
}

describe.each([
  {
    name: 'getSessionsSummary',
    call: getSessionsSummary,
    path: '/sessions/summary',
    body: { startedCount: 42 },
  },
  {
    name: 'getMessagesSummary',
    call: getMessagesSummary,
    path: '/messages/summary',
    body: { inboundCount: 128 },
  },
])('$name', ({ call, path, body }) => {
  it(`chama ${path} com os dois limites na query string`, async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body));
    vi.stubGlobal('fetch', fetchMock);

    await call(from, to);

    const url = calledUrl(fetchMock);
    expect(url.pathname).toBe(path);
    expect(url.searchParams.get('from')).toBe(from);
    expect(url.searchParams.get('to')).toBe(to);
  });

  it('codifica os limites em vez de mandar o ISO cru', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body));
    vi.stubGlobal('fetch', fetchMock);

    await call(from, to);

    const raw = fetchMock.mock.calls[0][0] as string;
    const query = raw.slice(raw.indexOf('?') + 1);
    expect(query).not.toContain(':');
    expect(query).toContain('from=2026-09-11T14%3A30%3A15.250Z');
  });

  it('devolve o corpo com o nome de campo do contrato', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(body)));

    await expect(call(from, to)).resolves.toEqual(body);
  });

  it('transforma resposta não-2xx em ApiError', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse({ title: 'One or more validation errors' }, 400)),
    );

    const error = await call(from, to).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
  });
});
