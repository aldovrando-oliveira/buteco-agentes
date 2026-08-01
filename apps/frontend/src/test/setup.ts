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

// jsdom não implementa ResizeObserver; o ScrollArea do Mantine usa isso para
// medir o conteúdo e decidir quando exibir a scrollbar.
if (!window.ResizeObserver) {
  window.ResizeObserver = class ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

// jsdom não implementa scrollIntoView; o Combobox do Mantine (usado pelo
// Select) chama isso ao abrir o dropdown para posicionar a opção ativa.
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {};
}

// jsdom não faz layout, o que quebra o middleware `hide` do floating-ui
// (usado pelo Popover/Combobox do Mantine para posicionar dropdowns) de
// duas formas que só juntas resolvem:
// 1. `document.documentElement.clientWidth/clientHeight` (o retângulo da
//    viewport, via `getViewportRect`) é 0 por padrão — qualquer elemento
//    fica "fora" dessa viewport degenerada.
// 2. `getBoundingClientRect` devolve um retângulo 0x0 encostado em (0,0) —
//    mesmo com a viewport corrigida, `getSideOffsets` subtrai a própria
//    largura/altura do elemento do cálculo de overflow; com width/height
//    zero essa margem some e o encosto exato em (0,0) já conta como
//    "clipado" (`overflow[lado] >= 0`).
// Sem os dois, `Select` nunca teria suas opções encontráveis em teste —
// o dropdown fica preso em `display: none` mesmo já aberto.
Object.defineProperty(document.documentElement, 'clientWidth', {
  configurable: true,
  value: 1024,
});
Object.defineProperty(document.documentElement, 'clientHeight', {
  configurable: true,
  value: 768,
});
Element.prototype.getBoundingClientRect = () => ({
  width: 100,
  height: 40,
  top: 10,
  left: 10,
  bottom: 50,
  right: 110,
  x: 10,
  y: 10,
  toJSON() {},
});

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
