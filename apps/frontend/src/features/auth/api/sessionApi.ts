import { retryAfterSeconds } from '../../../auth/transient';

// `POST /auth/session` do apps/api (#180): confere e-mail e senha e grava o cookie de
// sessão do emissor, que o authorize lê. Caminho relativo: em produção o nginx o leva
// ao apps/api, e em desenvolvimento o proxy do Vite, para o cookie ficar na origem do
// painel (design.md da change painel-entra-pelo-emissor, D10).
//
// Para quem tem a verificação em 2 etapas, a senha certa responde `200 { segundoPasso:
// "totp" }` com o cookie no passo pendente, e o código vai a
// `POST /auth/session/second-factor` (#218 D2; design.md da change
// entrada-publica-painel, D1).
//
// Sem token e sem `sessionFetch`: não há sessão antes do login. O resultado é um
// valor, e não um erro lançado, porque cada status tem a sua tela no /entrar (D6).

export interface SessionInput {
  email: string;
  senha: string;
}

export type HttpFailure = { kind: 'http'; status: number; title?: string; retryAfter?: number };

export type SessionResult =
  | { kind: 'ok' }
  | { kind: 'segundo-passo' }
  | HttpFailure
  | { kind: 'network' };

export type SecondFactorResult = { kind: 'ok' } | HttpFailure | { kind: 'network' };

/** `POST` com corpo JSON, sem token; `null` na falha de rede. */
export async function postJson(path: string, body: unknown): Promise<Response | null> {
  try {
    return await fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  } catch {
    return null;
  }
}

export async function httpFailure(response: Response): Promise<HttpFailure> {
  const problem = (await response.json().catch(() => undefined)) as { title?: unknown } | undefined;
  return {
    kind: 'http',
    status: response.status,
    title: typeof problem?.title === 'string' ? problem.title : undefined,
    retryAfter: retryAfterSeconds(response),
  };
}

export async function createSession(input: SessionInput): Promise<SessionResult> {
  const response = await postJson('/auth/session', input);
  if (response === null) return { kind: 'network' };
  if (response.status === 204) return { kind: 'ok' };

  if (response.status === 200) {
    const body = (await response.json().catch(() => undefined)) as { segundoPasso?: unknown } | undefined;
    if (body?.segundoPasso === 'totp') return { kind: 'segundo-passo' };
    return { kind: 'http', status: 200 };
  }

  return httpFailure(response);
}

/** O código do app ou um código de recuperação, no mesmo campo (#218 D6). */
export async function submitSecondFactor(codigo: string): Promise<SecondFactorResult> {
  const response = await postJson('/auth/session/second-factor', { codigo });
  if (response === null) return { kind: 'network' };
  if (response.status === 204) return { kind: 'ok' };
  return httpFailure(response);
}
