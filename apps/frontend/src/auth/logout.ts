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

export function createLogout(runtime: AuthRuntime, store: SessionStore) {
  return async function logout(): Promise<void> {
    const idToken = await runtime.locks().request(SESSION_LOCK, () => {
      const idToken = store.read()?.idToken || null;
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
