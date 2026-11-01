import { retryAfterSeconds } from '../../../auth/transient';

// `POST /auth/session` do apps/api (#180): confere e-mail e senha e grava o cookie de
// sessão do emissor, que o authorize lê. Caminho relativo: em produção o nginx o leva
// ao apps/api, e em desenvolvimento o proxy do Vite, para o cookie ficar na origem do
// painel (design.md da change painel-entra-pelo-emissor, D10).
//
// Sem token e sem `sessionFetch`: não há sessão antes do login. O resultado é um
// valor, e não um erro lançado, porque cada status tem a sua tela no /entrar (D6).

export interface SessionInput {
  email: string;
  senha: string;
}

export type SessionResult =
  | { kind: 'ok' }
  | { kind: 'http'; status: number; title?: string; retryAfter?: number }
  | { kind: 'network' };

export async function createSession(input: SessionInput): Promise<SessionResult> {
  let response: Response;
  try {
    response = await fetch('/auth/session', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
    });
  } catch {
    return { kind: 'network' };
  }

  if (response.status === 204) return { kind: 'ok' };

  const problem = (await response.json().catch(() => undefined)) as { title?: unknown } | undefined;
  return {
    kind: 'http',
    status: response.status,
    title: typeof problem?.title === 'string' ? problem.title : undefined,
    retryAfter: retryAfterSeconds(response),
  };
}
