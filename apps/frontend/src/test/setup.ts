import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';

// Com `test.globals: false`, o auto-cleanup do Testing Library (que depende
// de detectar um `afterEach` global) não é registrado sozinho — chamamos
// explicitamente para desmontar o componente renderizado após cada teste.
afterEach(() => {
  cleanup();
});

// jsdom não implementa matchMedia; o MantineProvider chama isso ao resolver
// o color scheme mesmo quando defaultColorScheme não é "auto".
if (!window.matchMedia) {
  window.matchMedia = (query: string) =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList;
}

// A partir do Node 22, `localStorage` é um global nativo (experimental) que
// exige a flag `--localstorage-file` para funcionar; sem ela, ele sombreia o
// `window.localStorage` do jsdom com uma implementação inutilizável.
// Substituímos por uma implementação em memória equivalente à da Storage API.
class MemoryStorage implements Storage {
  private store = new Map<string, string>();

  get length() {
    return this.store.size;
  }

  clear() {
    this.store.clear();
  }

  getItem(key: string) {
    return this.store.has(key) ? this.store.get(key)! : null;
  }

  key(index: number) {
    return Array.from(this.store.keys())[index] ?? null;
  }

  removeItem(key: string) {
    this.store.delete(key);
  }

  setItem(key: string, value: string) {
    this.store.set(key, String(value));
  }
}

Object.defineProperty(window, 'localStorage', {
  value: new MemoryStorage(),
  writable: true,
  configurable: true,
});
