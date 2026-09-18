import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  useChannelSessionsQuery,
  useMessagesSummaryQuery,
  useSessionMessagesQuery,
  useSessionsSummaryQuery,
} from './useSessions';
import {
  getMessagesSummary,
  getSessionMessages,
  getSessionsSummary,
  listChannelSessions,
} from './sessionsApi';
import type { ChannelSession, SessionMessage } from '../types/session';

vi.mock('./sessionsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./sessionsApi')>();
  return {
    ...actual,
    listChannelSessions: vi.fn(),
    getSessionMessages: vi.fn(),
    getSessionsSummary: vi.fn(),
    getMessagesSummary: vi.fn(),
  };
});

const session: ChannelSession = {
  sessionId: '11111111-1111-1111-1111-111111111111',
  contactId: '22222222-2222-2222-2222-222222222222',
  contactExternalId: '5511999999999',
  contactDisplayName: 'Maria',
  lastActivityAt: '2026-08-20T12:00:00Z',
  lastMessage: null,
};

const message: SessionMessage = {
  id: '33333333-3333-3333-3333-333333333333',
  direction: 'Inbound',
  content: 'Olá',
  contentType: 'Text',
  occurredAt: '2026-08-20T12:00:00Z',
  externalId: null,
  deliveryStatus: null,
  deliveryFailureReason: null,
  dispatchStatus: 'Pending',
};

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return {
    queryClient,
    Wrapper: ({ children }: PropsWithChildren) =>
      createElement(QueryClientProvider, { client: queryClient }, children),
  };
}

afterEach(() => {
  vi.useRealTimers();
});

describe('useSessionMessagesQuery', () => {
  beforeEach(() => {
    vi.mocked(getSessionMessages).mockReset();
    vi.mocked(getSessionMessages).mockResolvedValue([message]);
  });

  it('reconsulta após 5s decorridos enquanto a timeline está aberta', async () => {
    vi.useFakeTimers();
    const { Wrapper } = createWrapper();

    renderHook(() => useSessionMessagesQuery(session.sessionId), { wrapper: Wrapper });

    await vi.advanceTimersByTimeAsync(0);
    expect(getSessionMessages).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(5000);
    expect(getSessionMessages).toHaveBeenCalledTimes(2);
  });
});

describe('useChannelSessionsQuery', () => {
  beforeEach(() => {
    vi.mocked(listChannelSessions).mockReset();
    vi.mocked(listChannelSessions).mockResolvedValue([session]);
  });

  it('não reconsulta pela passagem do tempo sozinha', async () => {
    vi.useFakeTimers();
    const { Wrapper } = createWrapper();

    renderHook(() => useChannelSessionsQuery('channel-1'), { wrapper: Wrapper });

    await vi.advanceTimersByTimeAsync(0);
    expect(listChannelSessions).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(5000);
    expect(listChannelSessions).toHaveBeenCalledTimes(1);
  });
});

// A regra que estes testes protegem: a chave é FIXA e a janela nasce dentro do
// queryFn (design.md, D3). O relógio é movido com `vi.setSystemTime` entre os
// renders — é o que faria uma chave calculada no render mudar, e portanto o que
// faz o teste de "renders repetidos" pegar a regressão em vez de passar por
// acaso dentro do mesmo milissegundo.
describe.each([
  {
    name: 'useSessionsSummaryQuery',
    useHook: useSessionsSummaryQuery,
    client: getSessionsSummary,
    key: ['sessions', 'summary', 'last-7-days'],
    body: { startedCount: 42 },
  },
  {
    name: 'useMessagesSummaryQuery',
    useHook: useMessagesSummaryQuery,
    client: getMessagesSummary,
    key: ['messages', 'summary', 'last-7-days'],
    body: { inboundCount: 128 },
  },
] as const)('$name', ({ useHook, client, key, body }) => {
  const clientMock = () => vi.mocked(client as (from: string, to: string) => Promise<unknown>);

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-09-18T12:00:00.000Z'));
    clientMock().mockReset();
    clientMock().mockResolvedValue(body);
  });

  it('usa a chave fixa, sem os limites da janela dentro', async () => {
    const { queryClient, Wrapper } = createWrapper();
    const { result } = renderHook(() => useHook(), { wrapper: Wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const keys = queryClient
      .getQueryCache()
      .getAll()
      .map((q) => q.queryKey);
    expect(keys).toEqual([key]);
  });

  it('consulta os 7 dias que terminam no instante da consulta', async () => {
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useHook(), { wrapper: Wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(clientMock()).toHaveBeenCalledWith(
      '2026-09-11T12:00:00.000Z',
      '2026-09-18T12:00:00.000Z',
    );
    expect(result.current.data).toEqual(body);
  });

  it('uma nova consulta depois de o relógio andar envia a janela atualizada', async () => {
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useHook(), { wrapper: Wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    vi.setSystemTime(new Date('2026-09-18T15:30:00.000Z'));
    await result.current.refetch();

    expect(clientMock()).toHaveBeenCalledTimes(2);
    expect(clientMock()).toHaveBeenLastCalledWith(
      '2026-09-11T15:30:00.000Z',
      '2026-09-18T15:30:00.000Z',
    );
  });

  it('renders repetidos com o relógio andando não disparam consultas novas', async () => {
    const { Wrapper } = createWrapper();
    const { result, rerender } = renderHook(() => useHook(), { wrapper: Wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    for (const instant of ['12:00:01', '12:05:00', '13:00:00']) {
      vi.setSystemTime(new Date(`2026-09-18T${instant}.000Z`));
      rerender();
    }

    expect(clientMock()).toHaveBeenCalledTimes(1);
    expect(result.current.isSuccess).toBe(true);
  });
});
