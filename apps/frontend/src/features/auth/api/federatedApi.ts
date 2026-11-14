import { httpFailure, postJson, type HttpFailure } from './sessionApi';

// As rotas do login e do aceite de convite pelo provedor (#189 e #274; design.md da change
// telas-login-google, D1 a D3). Todas sob `/auth`, sem token, com o `Origin` que o navegador põe.
// O token do convite vai só no corpo do POST; nenhum token nem e-mail vai para URL ou log.

/**
 * Os `title` com que o backend separa os `404` do início (os dois só se distinguem por eles).
 * Uma constante só, para as páginas e os testes não repetirem o texto: o `federatedApi.test.ts`
 * confere cada um contra o valor do backend.
 */
export const START_404_TITLES = {
  /** `FederatedLoginEndpoints.NotConfiguredMessage` (o provedor saiu da configuração). */
  providerNotConfigured: 'Provedor não configurado.',
  /** `AccessLinkPublicEndpoints.InvalidLinkMessage` (o convite deixou de valer). */
  invalidLink:
    'Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador.',
} as const;

/** Os tipos de provedor que o painel sabe mostrar; outro tipo não ganha botão (convenção 13). */
export const KNOWN_PROVIDER_TYPES: Record<string, string> = { google: 'Google' };

export interface LoginProvider {
  chave: string;
  tipo: string;
  /** O nome do tipo ("Google"). */
  nome: string;
}

/**
 * `GET /auth/federated/providers`, só os provedores de tipo conhecido. Qualquer falha (rede, resposta
 * que não é `200`, corpo fora do formato) é a lista vazia: o bloco some sem aviso (Q5).
 */
export async function listLoginProviders(): Promise<LoginProvider[]> {
  let response: Response;
  try {
    response = await fetch('/auth/federated/providers');
  } catch {
    return [];
  }
  if (response.status !== 200) return [];
  const body = (await response.json().catch(() => undefined)) as
    { provedores?: unknown } | undefined;
  if (!Array.isArray(body?.provedores)) return [];
  return body.provedores.flatMap((item: unknown) => {
    const provider = item as { chave?: unknown; tipo?: unknown } | null;
    if (typeof provider?.chave !== 'string' || typeof provider.tipo !== 'string') return [];
    const nome = KNOWN_PROVIDER_TYPES[provider.tipo];
    return nome === undefined ? [] : [{ chave: provider.chave, tipo: provider.tipo, nome }];
  });
}

export type StartResult = { kind: 'ok'; url: string } | HttpFailure | { kind: 'network' };

async function start(path: string, body: unknown): Promise<StartResult> {
  const response = await postJson(path, body);
  if (response === null) return { kind: 'network' };
  if (response.status === 200) {
    const parsed = (await response.json().catch(() => undefined)) as { url?: unknown } | undefined;
    if (typeof parsed?.url === 'string') return { kind: 'ok', url: parsed.url };
    return { kind: 'http', status: 200 };
  }
  return httpFailure(response);
}

/** `POST /auth/federated/{chave}/start` com o `retorno` conferido do /entrar (ou `null`). */
export function startFederatedLogin(chave: string, retorno: string | null): Promise<StartResult> {
  return start(`/auth/federated/${encodeURIComponent(chave)}/start`, { retorno });
}

/** `POST /auth/invitation/federated/{chave}/start` com o token do convite no corpo. */
export function startFederatedInvitation(chave: string, token: string): Promise<StartResult> {
  return start(`/auth/invitation/federated/${encodeURIComponent(chave)}/start`, { token });
}

/** O conteúdo do `buteco-recusa`: o motivo, a chave do provedor e o e-mail que ele confirmou. */
export interface RefusalContent {
  motivo: string;
  provedor: string;
  email: string;
}

/**
 * `POST /auth/federated/refusal`, a rota de uso único do aviso da recusa: o motivo, o provedor e o
 * e-mail da recusa gravada, ou `null` (o `404` vazio, qualquer outra resposta, corpo fora do
 * formato, a rede). Quem lê confere o motivo e o provedor contra os da query (D2).
 */
export async function readRefusalEmail(): Promise<RefusalContent | null> {
  const response = await postJson('/auth/federated/refusal', {});
  if (response?.status !== 200) return null;
  const body = (await response.json().catch(() => undefined)) as
    { motivo?: unknown; provedor?: unknown; email?: unknown } | undefined;
  if (
    typeof body?.motivo !== 'string' ||
    typeof body.provedor !== 'string' ||
    typeof body.email !== 'string'
  ) {
    return null;
  }
  return { motivo: body.motivo, provedor: body.provedor, email: body.email };
}

/** O `404` do início pelo `title` exato; qualquer outro `404` é a falha genérica. */
export type Start404 = 'provider-not-configured' | 'invalid-link' | 'other';

export function classify404(result: HttpFailure): Start404 {
  if (result.title === START_404_TITLES.providerNotConfigured) return 'provider-not-configured';
  if (result.title === START_404_TITLES.invalidLink) return 'invalid-link';
  return 'other';
}
