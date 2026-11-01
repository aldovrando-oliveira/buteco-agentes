// O que o módulo de sessão usa do navegador, recebido por parâmetro (design.md da
// change painel-entra-pelo-emissor, D3 e D12). O jsdom não tem Web Locks nem várias
// abas: o teste monta duas "abas" sobre o mesmo storage e o mesmo gerenciador de lock
// falso, e o painel usa os do navegador.
//
// Tudo é lido na hora da chamada, e não na criação: os testes das features trocam o
// `fetch` global (`vi.stubGlobal`) depois de importar o módulo.

/** O pedaço do `navigator.locks` que a sessão usa: exclusão por nome. */
export interface LockManagerLike {
  request<T>(name: string, callback: () => Promise<T> | T): Promise<T>;
}

export interface AuthRuntime {
  /** O `localStorage`: o registro da sessão, visto por todas as abas. */
  storage(): Storage;
  /** O `sessionStorage`: o login em andamento, só da aba (D2). */
  tabStorage(): Storage;
  locks(): LockManagerLike;
  fetch(input: string, init?: RequestInit): Promise<Response>;
  now(): number;
  sleep(ms: number): Promise<void>;
  timeoutSignal(ms: number): AbortSignal;
  /** A origem do painel, para `redirect_uri` e `post_logout_redirect_uri`. */
  origin(): string;
  /** Caminho e query da página atual, para o `voltarPara`. */
  currentPath(): string;
  /** Navegação de página inteira: o authorize, o logout e o fim de sessão saem do SPA. */
  assign(url: string): void;
  /** O alvo do evento `storage`, disparado quando outra aba muda o registro. */
  events(): Pick<Window, 'addEventListener' | 'removeEventListener'>;
}

export function browserRuntime(): AuthRuntime {
  return {
    storage: () => window.localStorage,
    tabStorage: () => window.sessionStorage,
    locks: () => ({
      request: (name, callback) => navigator.locks.request(name, () => callback()),
    }),
    fetch: (input, init) => globalThis.fetch(input, init),
    now: () => Date.now(),
    sleep: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
    timeoutSignal: (ms) => AbortSignal.timeout(ms),
    origin: () => window.location.origin,
    currentPath: () => `${window.location.pathname}${window.location.search}`,
    assign: (url) => window.location.assign(url),
    events: () => window,
  };
}
