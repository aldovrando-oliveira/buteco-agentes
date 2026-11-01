// O fluxo de login pelo emissor (design.md da change painel-entra-pelo-emissor, D1 e
// D6): o /entrar grava a sessão do emissor com `POST /auth/session` e só então começa
// o authorize, com PKCE S256 e `state`. O login em andamento fica no sessionStorage da
// aba: não é sessão, e outra aba não deve vê-lo.
import { AUTHORIZE_ENDPOINT, CLIENT_ID, SCOPES, oauthError, postToken, redirectUri } from './oauth';
import { codeChallengeS256, createCodeVerifier, createState } from './pkce';
import type { Renewal } from './renewal';
import type { AuthRuntime } from './runtime';
import { isTokenResponseBody, sessionFromTokenResponse } from './sessionStore';
import { retryAfterSeconds } from './transient';

export const PENDING_LOGIN_KEY = 'buteco.login';
/** O código vale 5 min, e o cookie do emissor outros 5 (#180). */
export const PENDING_LOGIN_MAX_AGE_MS = 10 * 60_000;

export interface PendingLogin {
  verifier: string;
  state: string;
  voltarPara: string;
  criadoEm: number;
}

/**
 * `voltarPara` só como caminho do próprio painel: começa por `/`, não por `//`, sem
 * `\`, e não é o próprio /entrar. Qualquer outro valor vira `/`.
 */
export function safeReturnPath(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return '/';
  if (value === '/entrar' || value.startsWith('/entrar/') || value.startsWith('/entrar?')) return '/';
  return value;
}

/**
 * O `retorno` que o authorize manda ao /entrar (#180 D1), já decodificado uma vez pela
 * leitura da query. É seguido depois do `204` só quando começa por
 * `/connect/authorize?`; senão, `null`, e o painel vai à tela inicial, que começa um
 * fluxo novo com o cookie recém-gravado.
 *
 * O prefixo basta: um caminho que começa por `/connect/authorize?` não muda de origem
 * nem de caminho ao ser resolvido contra a origem do painel (design.md da change
 * painel-entra-pelo-emissor, D6; a conferência por `new URL` saiu na decisão do dono
 * de 01/11/2026, por ser redundante). A navegação usa exatamente a string conferida:
 * decodificar de novo ou recompor a URL seria seguir outro valor que não o conferido,
 * e o codificado duas vezes, que não começa pelo prefixo, passaria a começar.
 */
export function followableAuthorizeReturn(retorno: string | null): string | null {
  return retorno?.startsWith(`${AUTHORIZE_ENDPOINT}?`) ? retorno : null;
}

/** Grava o login em andamento (substituindo o anterior) e devolve a URL do authorize. */
export async function beginLogin(runtime: AuthRuntime, voltarPara: string): Promise<string> {
  const pending: PendingLogin = {
    verifier: createCodeVerifier(),
    state: createState(),
    voltarPara: safeReturnPath(voltarPara),
    criadoEm: runtime.now(),
  };
  runtime.tabStorage().setItem(PENDING_LOGIN_KEY, JSON.stringify(pending));

  const params = new URLSearchParams({
    response_type: 'code',
    client_id: CLIENT_ID,
    redirect_uri: redirectUri(runtime.origin()),
    scope: SCOPES,
    code_challenge: await codeChallengeS256(pending.verifier),
    code_challenge_method: 'S256',
    state: pending.state,
  });
  return `${AUTHORIZE_ENDPOINT}?${params.toString()}`;
}

/**
 * Lê e APAGA o login em andamento: é de uso único. Ausente, ilegível ou vencido,
 * `null`.
 */
export function takePendingLogin(runtime: AuthRuntime): PendingLogin | null {
  const raw = runtime.tabStorage().getItem(PENDING_LOGIN_KEY);
  runtime.tabStorage().removeItem(PENDING_LOGIN_KEY);
  if (raw === null) return null;
  try {
    const p = JSON.parse(raw) as Partial<PendingLogin> | null;
    if (
      typeof p?.verifier !== 'string' ||
      typeof p.state !== 'string' ||
      typeof p.voltarPara !== 'string' ||
      typeof p.criadoEm !== 'number'
    ) {
      return null;
    }
    if (runtime.now() - p.criadoEm > PENDING_LOGIN_MAX_AGE_MS) return null;
    return p as PendingLogin;
  } catch {
    return null;
  }
}

export type ExchangeOutcome = 'ok' | 'rejected' | 'unavailable';

/**
 * Troca o código. `200` grava a sessão sob o lock (D3). `invalid_client` (catálogo
 * ainda não sincronizado), `503` e falha de rede repetem uma vez depois do
 * `Retry-After`: o código é de resgate único (H5), e se a primeira resposta se perdeu
 * a repetição dá `invalid_grant`. Qualquer outro erro é `rejected`.
 */
export async function exchangeCode(
  runtime: AuthRuntime,
  renewal: Renewal,
  input: { code: string; verifier: string },
): Promise<ExchangeOutcome> {
  for (let attempt = 0; ; attempt++) {
    let response: Response | null;
    try {
      response = await postToken(runtime, {
        grant_type: 'authorization_code',
        code: input.code,
        redirect_uri: redirectUri(runtime.origin()),
        client_id: CLIENT_ID,
        code_verifier: input.verifier,
      });
    } catch {
      response = null;
    }

    if (response?.ok) {
      const body: unknown = await response.json().catch(() => undefined);
      if (!isTokenResponseBody(body)) return 'unavailable';
      await renewal.storeNewSession(sessionFromTokenResponse(body, runtime.now()));
      return 'ok';
    }

    const transient =
      response === null ||
      response.status === 503 ||
      (await oauthError(response)) === 'invalid_client';
    if (!transient) return 'rejected';
    if (attempt >= 1) return 'unavailable';
    await runtime.sleep(((response && retryAfterSeconds(response)) ?? 1) * 1000);
  }
}
