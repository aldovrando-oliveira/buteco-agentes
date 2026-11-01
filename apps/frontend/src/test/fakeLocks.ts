import type { LockManagerLike } from '../auth/runtime';

// Gerenciador de lock de teste COM exclusão mútua real: o pedido seguinte pelo mesmo
// nome só começa quando o anterior termina, como o `navigator.locks` faz entre as abas
// de uma origem. Duas "abas" de teste compartilham a mesma instância.
//
// Um dublê que só chamasse o callback deixaria o teste das renovações simultâneas
// passar sem coordenação nenhuma; o `fakeLocks.test.ts` é o guarda disso (design.md
// da change painel-entra-pelo-emissor, D12).
export class FakeLockManager implements LockManagerLike {
  private readonly tails = new Map<string, Promise<unknown>>();

  request<T>(name: string, callback: () => Promise<T> | T): Promise<T> {
    const previous = this.tails.get(name) ?? Promise.resolve();
    const run = previous.then(() => callback());
    this.tails.set(
      name,
      run.catch(() => undefined),
    );
    return run;
  }
}

/** Promessa que o teste resolve quando quiser: segura uma resposta "em voo". */
export function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}
