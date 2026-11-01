// O que o painel conhece do emissor (#180): o cliente público `buteco-painel`, os
// escopos dos três apps e os endpoints, sempre por caminho relativo. Em produção, o
// nginx leva `/connect` e `/auth` ao apps/api; em desenvolvimento, o proxy do Vite
// (design.md da change painel-entra-pelo-emissor, D10). O painel não lê o discovery.
import type { AuthRuntime } from './runtime';

export const CLIENT_ID = 'buteco-painel';
export const SCOPES = 'openid offline_access buteco-api buteco-inbox buteco-connectors';
export const AUTHORIZE_ENDPOINT = '/connect/authorize';
export const TOKEN_ENDPOINT = '/connect/token';
export const LOGOUT_ENDPOINT = '/connect/logout';

export function redirectUri(origin: string): string {
  return `${origin}/entrar/retorno`;
}

export function postToken(
  runtime: AuthRuntime,
  form: Record<string, string>,
  signal?: AbortSignal,
): Promise<Response> {
  return runtime.fetch(TOKEN_ENDPOINT, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams(form).toString(),
    signal,
  });
}

/** O `error` do corpo OAuth (`invalid_grant`, `invalid_client`…), ou `undefined`. */
export async function oauthError(response: Response): Promise<string | undefined> {
  try {
    const body: unknown = await response.clone().json();
    const error = (body as { error?: unknown } | null)?.error;
    return typeof error === 'string' ? error : undefined;
  } catch {
    return undefined;
  }
}
