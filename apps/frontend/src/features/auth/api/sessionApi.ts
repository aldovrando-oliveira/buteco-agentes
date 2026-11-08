import { retryAfterSeconds } from '../../../auth/transient';

// `POST /auth/session` do apps/api (#180): confere e-mail e senha e grava o cookie de
// sessão do emissor, que o authorize lê. Caminho relativo: em produção o nginx o leva
// ao apps/api, e em desenvolvimento o proxy do Vite, para o cookie ficar na origem do
// painel (design.md da change painel-entra-pelo-emissor, D10).
//
// A verificação em 2 etapas é obrigatória (design.md da change verificacao-2-etapas-obrigatoria,
// D1): a senha certa responde sempre `200` com o cookie no passo pendente. Com `{ segundoPasso:
// "totp" }`, o código vai a `POST /auth/session/second-factor` (#218 D2; design.md da change
// entrada-publica-painel, D1); com `{ segundoPasso: "cadastro" }`, a pessoa cadastra o app em
// `POST /auth/session/enrollment/setup` e `/confirm`, e só então tem a sessão.
//
// Sem token e sem `sessionFetch`: não há sessão antes do login. O resultado é um
// valor, e não um erro lançado, porque cada status tem a sua tela no /entrar (D6).

export interface SessionInput {
  email: string;
  senha: string;
}

export type HttpFailure = { kind: 'http'; status: number; title?: string; retryAfter?: number };

/** O segundo passo que a senha certa abre: o código do app, ou o cadastro dele. */
export type SecondStep = 'totp' | 'cadastro';

export type SessionResult =
  | { kind: 'segundo-passo'; passo: SecondStep }
  | HttpFailure
  | { kind: 'network' };

export type SecondFactorResult = { kind: 'ok' } | HttpFailure | { kind: 'network' };

export type EnrollmentStartResult =
  | { kind: 'ok'; segredo: string; otpauthUri: string }
  | HttpFailure
  | { kind: 'network' };

export type EnrollmentConfirmResult = { kind: 'ok'; codigosRecuperacao: string[] } | HttpFailure | { kind: 'network' };

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

  // Não há `204` nesta rota desde a obrigatoriedade: um `204` cai no aviso genérico, como
  // qualquer resposta que o painel não conhece.
  if (response.status === 200) {
    const body = (await response.json().catch(() => undefined)) as { segundoPasso?: unknown } | undefined;
    if (body?.segundoPasso === 'totp' || body?.segundoPasso === 'cadastro') {
      return { kind: 'segundo-passo', passo: body.segundoPasso };
    }
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

/**
 * O passo 1 do cadastro no login: um segredo novo para o usuário do cookie pendente, sem token
 * (design.md da change verificacao-2-etapas-obrigatoria, D1). Chamar de novo troca o segredo, e é
 * o que o "Tentar de novo" do QR faz. O segredo e o URI ficam só na memória da etapa.
 */
export async function startEnrollment(): Promise<EnrollmentStartResult> {
  const response = await postJson('/auth/session/enrollment/setup', {});
  if (response === null) return { kind: 'network' };
  if (response.status === 200) {
    const body = (await response.json().catch(() => undefined)) as { segredo?: unknown; otpauthUri?: unknown } | undefined;
    if (typeof body?.segredo === 'string' && typeof body.otpauthUri === 'string') {
      return { kind: 'ok', segredo: body.segredo, otpauthUri: body.otpauthUri };
    }
    return { kind: 'http', status: 200 };
  }
  return httpFailure(response);
}

/** O passo 2 do cadastro: o código do app liga a verificação, devolve os 10 códigos e completa a sessão. */
export async function confirmEnrollment(codigo: string): Promise<EnrollmentConfirmResult> {
  const response = await postJson('/auth/session/enrollment/confirm', { codigo });
  if (response === null) return { kind: 'network' };
  if (response.status === 200) {
    const body = (await response.json().catch(() => undefined)) as { codigosRecuperacao?: unknown } | undefined;
    const codigos = body?.codigosRecuperacao;
    if (Array.isArray(codigos) && codigos.every((codigo) => typeof codigo === 'string')) {
      return { kind: 'ok', codigosRecuperacao: codigos };
    }
    return { kind: 'http', status: 200 };
  }
  return httpFailure(response);
}
