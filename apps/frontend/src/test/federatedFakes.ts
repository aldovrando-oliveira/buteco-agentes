import { jsonResponse } from './authFakes';

// O duplo das rotas federadas do apps/api para os testes do auth (design.md da change
// telas-login-google, D1 a D3): a lista dos provedores, os dois inícios e a rota do aviso da recusa,
// de uso único como a do servidor (a segunda chamada responde 404 vazio).

export const GOOGLE = { chave: 'corporativo', tipo: 'google' };

export const PROVIDERS_PATH = '/auth/federated/providers';
export const REFUSAL_PATH = '/auth/federated/refusal';
export const LOGIN_START_PATH = '/auth/federated/corporativo/start';
export const INVITATION_START_PATH = '/auth/invitation/federated/corporativo/start';
export const GOOGLE_URL = 'https://accounts.google.com/o/oauth2/v2/auth?client_id=x&state=s';

/** `GET /auth/federated/providers` com os provedores dados (padrão: o Google). */
export const providersResponse = (provedores: unknown = [GOOGLE]) =>
  jsonResponse({ provedores }, 200);

/** O `200 { url }` do início. */
export const startOk = (url = GOOGLE_URL) => jsonResponse({ url }, 200);

/** Um `404` com o `title` dado (sem `title`, o corpo vazio). */
export const notFoundWith = (title?: string) =>
  title === undefined ? new Response(null, { status: 404 }) : jsonResponse({ title }, 404);

/**
 * A rota do aviso, de uso único: a primeira chamada responde o aviso com o e-mail, e as seguintes
 * `404` vazio, como o cookie `buteco-recusa` lido no servidor.
 */
export function refusalRoute(body: { motivo: string; provedor: string; email: string }) {
  let lido = false;
  return () => {
    if (lido) return new Response(null, { status: 404 });
    lido = true;
    return jsonResponse(body, 200);
  };
}
