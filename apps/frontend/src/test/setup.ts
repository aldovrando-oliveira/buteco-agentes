import { afterEach } from 'vitest';
import { cleanup, configure } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryStorage } from './authFakes';
import { FakeLockManager } from './fakeLocks';

// O padrão de 1s das consultas assíncronas do Testing Library é curto para esta
// suíte: os formulários abrem dropdowns do Mantine, que montam em portal depois
// de uma transição, enquanto cinquenta e quatro arquivos de teste rodam em
// paralelo. Sob contenção, a opção ainda não existe quando o prazo acaba.
//
// Era a causa da instabilidade que apareceu desde a etapa da casca do painel:
// falhas em testes de formulário que somem quando rodados isolados. Não é
// lógica de teste errada — os helpers já usam consulta assíncrona —, é prazo
// padrão apertado.
configure({ asyncUtilTimeout: 5000 });

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

// A partir do Node 22, `localStorage`/`sessionStorage` são globais nativos
// (experimentais) que exigem flag para funcionar; sem ela, sombreiam as
// implementações do jsdom com algo inutilizável. Substituímos os dois por
// uma implementação em memória equivalente à da Storage API — o registro da
// sessão vive no localStorage, e o login em andamento no sessionStorage
// (design.md da change painel-entra-pelo-emissor, D2).
Object.defineProperty(window, 'localStorage', {
  value: new MemoryStorage(),
  writable: true,
  configurable: true,
});
Object.defineProperty(window, 'sessionStorage', {
  value: new MemoryStorage(),
  writable: true,
  configurable: true,
});

// jsdom não implementa Web Locks (medido em 31/10/2026, design.md da change
// painel-entra-pelo-emissor, Context). O módulo de sessão pede o lock da renovação
// pelo `navigator.locks`; o falso tem exclusão mútua real, como o do navegador.
Object.defineProperty(navigator, 'locks', {
  value: new FakeLockManager(),
  writable: true,
  configurable: true,
});
