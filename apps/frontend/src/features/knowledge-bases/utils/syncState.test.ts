import { describe, expect, it } from 'vitest';
import {
  SYNC_POLL_INTERVAL_MS,
  SYNC_WAIT_LIMIT_MS,
  formatSyncInstant,
  isSyncFailing,
  syncRequestPhase,
  syncStatus,
  syncWaitInterval,
} from './syncState';
import type { KnowledgeBase, KnowledgeBaseSyncState } from '../types/knowledgeBase';

const NEVER: KnowledgeBaseSyncState = {
  lastCompletedAt: null,
  lastFinishedAt: null,
  failingSince: null,
  lastError: null,
  ignoredFiles: null,
};

function state(overrides: Partial<KnowledgeBaseSyncState>): KnowledgeBaseSyncState {
  return { ...NEVER, ...overrides };
}

function base(syncState: KnowledgeBaseSyncState | null): KnowledgeBase {
  return {
    id: 'b',
    name: 'Base',
    description: 'd',
    isActive: true,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    contentMode: syncState ? 'Synced' : 'Manual',
    syncSource: syncState
      ? { provider: 'google-drive', folderId: 'f', folderName: 'F', folderUrl: 'https://x' }
      : null,
    syncState,
  };
}

describe('syncStatus', () => {
  it('nunca sincronizou: os três instantes nulos', () => {
    expect(syncStatus(NEVER)).toBe('never-synced');
  });

  it('em dia: concluída preenchida e sem falha', () => {
    expect(
      syncStatus(
        state({ lastCompletedAt: '2026-10-01T10:00:00Z', lastFinishedAt: '2026-10-01T10:00:00Z' }),
      ),
    ).toBe('up-to-date');
  });

  it('falhando depois de uma sincronização concluída', () => {
    expect(
      syncStatus(
        state({
          lastCompletedAt: '2026-10-01T10:00:00Z',
          lastFinishedAt: '2026-10-01T10:10:00Z',
          failingSince: '2026-10-01T10:05:00Z',
          lastError: { code: 'access-denied', detail: 'x@y' },
        }),
      ),
    ).toBe('failing');
  });

  it('falhando sem nunca ter concluído', () => {
    expect(
      syncStatus(
        state({
          lastFinishedAt: '2026-10-01T10:00:00Z',
          failingSince: '2026-10-01T10:00:00Z',
          lastError: { code: 'rate-limited', detail: null },
        }),
      ),
    ).toBe('failing-never-completed');
  });

  // Estado inalcançável pela regra do apps/api (erro e "falhando desde" são
  // gravados e limpos juntos). O predicado de falha é um só, `failingSince`.
  it('erro sem failingSince é tratado como em dia', () => {
    expect(
      syncStatus(
        state({
          lastCompletedAt: '2026-10-01T10:00:00Z',
          lastFinishedAt: '2026-10-01T10:00:00Z',
          lastError: { code: 'provider-error', detail: null },
        }),
      ),
    ).toBe('up-to-date');
  });
});

describe('isSyncFailing', () => {
  it('é verdadeiro só com failingSince preenchido', () => {
    expect(isSyncFailing(base(state({ failingSince: '2026-10-01T10:00:00Z' })))).toBe(true);
    expect(isSyncFailing(base(NEVER))).toBe(false);
    expect(isSyncFailing(base(state({ lastError: { code: 'x', detail: null } })))).toBe(false);
  });

  it('base manual nunca está falhando', () => {
    expect(isSyncFailing(base(null))).toBe(false);
  });
});

describe('formatSyncInstant', () => {
  it('data com ano e hora, no formato pt-BR', () => {
    expect(formatSyncInstant('2026-09-25T12:10:00Z')).toMatch(/^\d{2}\/\d{2}\/\d{4}, \d{2}:\d{2}$/);
  });
});

describe('syncWaitInterval', () => {
  const T0 = 1_000_000;
  const request = { baseline: '2026-10-01T10:00:00Z', requestedAt: T0 };

  it('sem pedido, não consulta', () => {
    expect(syncWaitInterval(null, base(state({ lastFinishedAt: 'x' })), T0)).toBe(false);
  });

  it('linha de base igual e dentro do limite: consulta a cada 4 s', () => {
    const current = base(state({ lastFinishedAt: '2026-10-01T10:00:00Z' }));
    expect(syncWaitInterval(request, current, T0 + 1000)).toBe(SYNC_POLL_INTERVAL_MS);
    expect(SYNC_POLL_INTERVAL_MS).toBe(4000);
  });

  it('lastFinishedAt diferente da linha de base: para', () => {
    const current = base(state({ lastFinishedAt: '2026-10-01T10:00:09Z' }));
    expect(syncWaitInterval(request, current, T0 + 1000)).toBe(false);
  });

  it('linha de base nula que passa a ter valor: para', () => {
    const fromNever = { baseline: null, requestedAt: T0 };
    expect(syncWaitInterval(fromNever, base(NEVER), T0 + 1000)).toBe(SYNC_POLL_INTERVAL_MS);
    expect(
      syncWaitInterval(
        fromNever,
        base(state({ lastFinishedAt: '2026-10-01T10:00:00Z' })),
        T0 + 1000,
      ),
    ).toBe(false);
  });

  it('além do limite de 5 minutos: para', () => {
    const current = base(state({ lastFinishedAt: '2026-10-01T10:00:00Z' }));
    expect(SYNC_WAIT_LIMIT_MS).toBe(5 * 60 * 1000);
    expect(syncWaitInterval(request, current, T0 + SYNC_WAIT_LIMIT_MS - 1)).toBe(
      SYNC_POLL_INTERVAL_MS,
    );
    expect(syncWaitInterval(request, current, T0 + SYNC_WAIT_LIMIT_MS)).toBe(false);
  });

  it('sem a base em mãos, continua esperando dentro do limite', () => {
    expect(syncWaitInterval(request, undefined, T0 + 1000)).toBe(SYNC_POLL_INTERVAL_MS);
  });
});

describe('syncRequestPhase', () => {
  const T0 = 1_000_000;
  const request = { baseline: '2026-10-01T10:00:00Z', requestedAt: T0 };

  it('espera, termina pela mudança, e termina por limite', () => {
    const same = base(state({ lastFinishedAt: '2026-10-01T10:00:00Z' }));
    const changed = base(state({ lastFinishedAt: '2026-10-01T10:00:09Z' }));
    expect(syncRequestPhase(request, same, T0 + 1000)).toBe('waiting');
    expect(syncRequestPhase(request, changed, T0 + 1000)).toBe('finished');
    expect(syncRequestPhase(request, same, T0 + SYNC_WAIT_LIMIT_MS)).toBe('timed-out');
    // A mudança vence o limite: um resultado gravado nunca é lido como "sem resultado".
    expect(syncRequestPhase(request, changed, T0 + SYNC_WAIT_LIMIT_MS + 1)).toBe('finished');
  });
});
