// "Sair" (design.md da change painel-entra-pelo-emissor, D7). Sob o lock da renovação:
// uma renovação em voo termina antes, o `idToken` lido é o mais novo (o que o
// `id_token_hint` pede), e nenhuma resposta atrasada regrava a sessão depois de
// apagada (D3). O evento `storage` leva as outras abas ao /entrar.
//
// O apps/api revoga a autorização do hint e apaga o cookie do emissor. O access token
// já emitido vale até vencer, nos três apps (aceito na M1).
import { LOGOUT_ENDPOINT } from './oauth';
import { SESSION_LOCK } from './renewal';
import type { AuthRuntime } from './runtime';
import type { SessionStore } from './sessionStore';

/**
 * O logout desta aba em andamento (design.md da change minha-conta-painel, D3; decisão do
 * dono, opção 3). O registro sai antes de a navegação ao `/connect/logout` descarregar a
 * página, e sem esta marcação o `ProtectedRoute` levaria o SPA ao `/entrar?voltarPara=…`
 * nesse intervalo: o `/entrar` interno aparecia por um instante e consumia o aviso
 * "Senha alterada." antes do `/entrar` de verdade. Com ela, a rota protegida mostra só
 * "Saindo…" até o navegador sair. É desta aba: as outras recebem o evento `storage` e vão
 * ao `/entrar` como antes. A página sai, então a marcação nunca precisa voltar.
 */
export interface LeavingFlag {
  mark(): void;
  isLeaving(): boolean;
  subscribe(listener: () => void): () => void;
  /** Só para testes, que reaproveitam o mesmo módulo entre casos. */
  reset(): void;
}

export function createLeavingFlag(): LeavingFlag {
  let leaving = false;
  const listeners = new Set<() => void>();
  const notify = () => listeners.forEach((listener) => listener());
  return {
    mark() {
      leaving = true;
      notify();
    },
    isLeaving: () => leaving,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    reset() {
      leaving = false;
      notify();
    },
  };
}

export function createLogout(runtime: AuthRuntime, store: SessionStore, leaving: LeavingFlag = createLeavingFlag()) {
  return async function logout(): Promise<void> {
    const idToken = await runtime.locks().request(SESSION_LOCK, () => {
      const idToken = store.read()?.idToken || null;
      // Antes de apagar: quem reage ao registro apagado já encontra a aba saindo.
      leaving.mark();
      store.remove();
      return idToken;
    });

    const params = new URLSearchParams();
    // Sem id token (registro corrompido), o apps/api apaga o cookie e manda ao
    // /entrar, mas a autorização fica viva até vencer. É defeito, e não uso.
    if (idToken) params.set('id_token_hint', idToken);
    params.set('post_logout_redirect_uri', `${runtime.origin()}/entrar`);
    runtime.assign(`${LOGOUT_ENDPOINT}?${params.toString()}`);
  };
}
