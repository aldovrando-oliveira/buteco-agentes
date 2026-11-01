// Renovação coordenada entre abas (design.md da change painel-entra-pelo-emissor, D3).
//
// O emissor aceita o mesmo refresh resgatado por mais 30 s (OpenIddict 7.7.2,
// `RefreshTokenReuseLeeway`, H1/H2). Depois disso, reapresentá-lo revoga todos os
// tokens da autorização, inclusive o refresh mais novo de outra aba (H3). Por isso o
// refresh nunca sai do registro para uma variável que viva mais que a renovação: ela
// roda sob um Web Lock exclusivo e relê o registro dentro dele.
//
// O mesmo lock é o de todo escritor do registro: a troca do código, a renovação, o
// logout e o fim de sessão. Sem isso, a resposta de uma renovação em voo regravaria a
// sessão que outra aba acabou de encerrar (D3, revisão do dono de 01/11/2026).
import { CLIENT_ID, oauthError, postToken } from './oauth';
import type { AuthRuntime } from './runtime';
import { isTokenResponseBody, sessionFromTokenResponse } from './sessionStore';
import type { Session, SessionStore } from './sessionStore';
import { retryAfterSeconds } from './transient';

export const SESSION_LOCK = 'buteco.sessao.renovar';
/** Renova antes do pedido se o access token vence em menos disto (D4). */
export const RENEW_MARGIN_MS = 60_000;
/**
 * Nenhuma tentativa começa depois disto da primeira: o mesmo refresh só é reapresentado
 * dentro da tolerância de 30 s do emissor (H1), com folga para o pedido chegar.
 */
export const RETRY_WINDOW_MS = 25_000;
export const ATTEMPT_TIMEOUT_MS = 10_000;

export type RenewalOutcome =
  | { kind: 'ok'; session: Session }
  /** A sessão acabou: registro ausente, ou `invalid_grant` (já apagado). */
  | { kind: 'ended' }
  /** O emissor não respondeu a tempo; o registro continua, e a próxima chamada tenta de novo. */
  | { kind: 'unavailable' };

export interface Renewal {
  /** Renova sob o lock. `failedAccessToken` é o token que levou `401`, se houve. */
  renew(failedAccessToken?: string): Promise<RenewalOutcome>;
  /** A sessão para o próximo pedido, renovada antes se vence em menos de 60 s. */
  ensureFresh(): Promise<Session | null>;
  /** Grava a sessão de um login novo, sob o lock. */
  storeNewSession(session: Session): Promise<void>;
  /** Fim de sessão pelo segundo `401`: apaga só se o registro ainda é o que falhou. */
  endSessionIfCurrent(accessToken: string): Promise<void>;
}

export function createRenewal(runtime: AuthRuntime, store: SessionStore): Renewal {
  const withLock = <T>(callback: () => Promise<T> | T) =>
    runtime.locks().request(SESSION_LOCK, callback);

  async function refreshUnderLock(current: Session): Promise<RenewalOutcome> {
    const firstAttemptAt = runtime.now();
    for (;;) {
      let response: Response | null;
      try {
        response = await postToken(
          runtime,
          { grant_type: 'refresh_token', refresh_token: current.refreshToken, client_id: CLIENT_ID },
          runtime.timeoutSignal(ATTEMPT_TIMEOUT_MS),
        );
      } catch {
        // Falha de rede ou prazo: o pedido pode ter chegado e resgatado o refresh
        // (H5). Repetir dentro da janela é seguro; fora dela, não.
        response = null;
      }

      if (response?.ok) {
        const body: unknown = await response.json().catch(() => undefined);
        if (!isTokenResponseBody(body)) return { kind: 'unavailable' };
        const next = sessionFromTokenResponse(body, runtime.now(), current);
        store.write(next);
        return { kind: 'ok', session: next };
      }

      if (response && response.status !== 503) {
        if (response.status === 400 && (await oauthError(response)) === 'invalid_grant') {
          // Ainda dentro do lock: apaga só o registro com o refresh que falhou.
          if (store.read()?.refreshToken === current.refreshToken) store.remove();
          return { kind: 'ended' };
        }
        return { kind: 'unavailable' };
      }

      // 503 (o gate do anel, antes do OpenIddict: o refresh não foi resgatado) ou rede.
      const waitMs = ((response && retryAfterSeconds(response)) ?? 1) * 1000;
      if (runtime.now() + waitMs - firstAttemptAt >= RETRY_WINDOW_MS) return { kind: 'unavailable' };
      await runtime.sleep(waitMs);
    }
  }

  const renew = (failedAccessToken?: string) =>
    withLock<RenewalOutcome>(async () => {
      const current = store.read();
      if (current === null) return { kind: 'ended' };
      // Outra aba já renovou enquanto esta esperava o lock: usa o que ela gravou.
      if (
        current.accessToken !== failedAccessToken &&
        current.accessExpiresAt > runtime.now() + RENEW_MARGIN_MS
      ) {
        return { kind: 'ok', session: current };
      }
      return refreshUnderLock(current);
    });

  return {
    renew,

    async ensureFresh() {
      const current = store.read();
      if (current === null) return null;
      if (current.accessExpiresAt > runtime.now() + RENEW_MARGIN_MS) return current;
      const outcome = await renew();
      if (outcome.kind === 'ok') return outcome.session;
      return outcome.kind === 'unavailable' ? store.read() : null;
    },

    storeNewSession: (session) => withLock(() => store.write(session)),

    endSessionIfCurrent: (accessToken) =>
      withLock(() => {
        if (store.read()?.accessToken === accessToken) store.remove();
      }),
  };
}
