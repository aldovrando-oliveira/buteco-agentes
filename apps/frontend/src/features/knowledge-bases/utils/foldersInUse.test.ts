import { describe, expect, it } from 'vitest';
import { buildFoldersInUse, folderInUseBy } from './foldersInUse';
import type { KnowledgeBase } from '../types/knowledgeBase';

function base(overrides: Partial<KnowledgeBase>): KnowledgeBase {
  return {
    id: 'b',
    name: 'Base',
    description: 'd',
    isActive: true,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    contentMode: 'Manual',
    syncSource: null,
    syncState: null,
    ...overrides,
  };
}

function synced(name: string, provider: string, folderId: string, isActive = true): KnowledgeBase {
  return base({
    id: `b-${folderId}`,
    name,
    isActive,
    contentMode: 'Synced',
    syncSource: { provider, folderId, folderName: folderId, folderUrl: 'https://x' },
    syncState: {
      lastCompletedAt: null,
      lastFinishedAt: null,
      failingSince: null,
      lastError: null,
      ignoredFiles: null,
    },
  });
}

describe('buildFoldersInUse', () => {
  it('mapeia a pasta de cada base sincronizada, ativa ou inativa, para o nome da base', () => {
    const map = buildFoldersInUse([
      synced('Políticas internas de RH', 'google-drive', 'f-rh'),
      synced('FAQ antigo', 'google-drive', 'f-faq', false),
    ]);

    expect(folderInUseBy(map, 'google-drive', 'f-rh')).toBe('Políticas internas de RH');
    expect(folderInUseBy(map, 'google-drive', 'f-faq')).toBe('FAQ antigo');
  });

  it('base manual não ocupa pasta nenhuma', () => {
    const map = buildFoldersInUse([base({ name: 'Manual' })]);
    expect(map.size).toBe(0);
  });

  it('ids que diferem só na caixa são pastas diferentes, como no índice do apps/api', () => {
    const map = buildFoldersInUse([synced('Base AbC', 'google-drive', 'AbC')]);

    expect(folderInUseBy(map, 'google-drive', 'AbC')).toBe('Base AbC');
    expect(folderInUseBy(map, 'google-drive', 'abc')).toBeUndefined();
  });

  it('a mesma pasta em outro provedor não está em uso', () => {
    const map = buildFoldersInUse([synced('Base', 'google-drive', 'f-1')]);
    expect(folderInUseBy(map, 'onedrive', 'f-1')).toBeUndefined();
  });
});
