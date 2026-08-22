import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useChannelSessionsQuery, useSessionMessagesQuery } from './useSessions';
import { getSessionMessages, listChannelSessions } from './sessionsApi';
import type { ChannelSession, SessionMessage } from '../types/session';

vi.mock('./sessionsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./sessionsApi')>();
  return { ...actual, listChannelSessions: vi.fn(), getSessionMessages: vi.fn() };
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
