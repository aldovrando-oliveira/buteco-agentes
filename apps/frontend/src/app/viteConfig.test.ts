import { describe, expect, it } from 'vitest';
import config from '../../vite.config';

describe('vite.config: proxy do fluxo do emissor', () => {
  it('leva /auth, /connect e /.well-known ao apps/api local, nenhum com changeOrigin', () => {
    const proxy = config.server?.proxy as Record<string, { target: string; changeOrigin?: boolean }>;

    expect(Object.keys(proxy).sort()).toEqual(['/.well-known', '/auth', '/connect']);
    for (const entry of Object.values(proxy)) {
      expect(entry.target).toBe('http://localhost:5017');
      expect(entry.changeOrigin).toBeUndefined();
    }
  });
});
