import { sessionFetch } from '../../../auth/session';
import { retryAfterSeconds } from '../../../auth/transient';

// `request<T>` E `ApiError` PRÓPRIOS DA FEATURE, copiados do idioma de `agentsApi.ts`. Não é
// descuido nem duplicação a corrigir: é regra escrita da casa (convenção 2 — esperar 2-3
// consumidores REAIS antes de extrair). Extrair um cliente compartilhado é change própria,
// com todas as cópias na mesa — não efeito colateral de uma tela nova.
//
// As rotas são as de Minha conta no apps/api (design.md da change minha-conta-painel, D2):
// `/account` e `/account/sessions` (#219) e `/account/two-factor` (#218). O `401` de senha
// ou código errado chega aqui como `ApiError` com o `title` do servidor, sem deslogar: o
// `sessionFetch` o devolve sem renovar (D10; decisão do dono Q0).

export interface ValidationProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ValidationProblemDetails;
  /** O `Retry-After`, em segundos: o `423` do bloqueio, o `429` e o `503`. */
  readonly retryAfter?: number;
  /** O `title` do servidor, quando a resposta trouxe um. */
  readonly title?: string;

  constructor(status: number, message: string, problem?: ValidationProblemDetails, retryAfter?: number) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.retryAfter = retryAfter;
    this.title = problem?.title;
  }
}

// Default alinhado ao profile "http" do launchSettings.json de apps/api.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5017';

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  // O token, a renovação e o `401` são do `sessionFetch` (design.md da change
  // painel-entra-pelo-emissor, D5 e D13); a URL, o erro e o corpo continuam daqui.
  const response = await sessionFetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  });

  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as ValidationProblemDetails | undefined;
    throw new ApiError(
      response.status,
      problem?.title ?? `Erro ${response.status} ao chamar ${path}`,
      problem,
      retryAfterSeconds(response),
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

const post = <T>(path: string, body?: unknown) =>
  request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) });

// --- Dados e senha (#219) ---

export interface Account {
  id: string;
  nome: string;
  email: string;
  criadoEm: string;
  ultimoAcesso: string | null;
  duasEtapas: boolean;
}

export function getAccount(): Promise<Account> {
  return request<Account>('/account');
}

/** `204`; `400` política; `401` "Senha atual inválida."; `423` + `Retry-After`. */
export function changePassword(senhaAtual: string, novaSenha: string): Promise<void> {
  return post<void>('/account/password', { senhaAtual, novaSenha });
}

// --- Verificação em 2 etapas (#218) ---

export interface TwoFactorStatus {
  ativa: boolean;
  ativadaEm: string | null;
  codigosRestantes: number;
  cadastroDisponivel: boolean;
}

export interface TwoFactorSetup {
  segredo: string;
  otpauthUri: string;
}

export interface RecoveryCodes {
  codigosRecuperacao: string[];
}

export function getTwoFactor(): Promise<TwoFactorStatus> {
  return request<TwoFactorStatus>('/account/two-factor');
}

/** `200` o segredo, uma vez; `401` "Senha inválida."; `403` cadastro fechado; `409` já ligada; `423`. */
export function setupTwoFactor(senha: string): Promise<TwoFactorSetup> {
  return post<TwoFactorSetup>('/account/two-factor/setup', { senha });
}

/** `200` os 10 códigos, uma vez; `400` cadastro vencido; `401` código; `403`; `423`. */
export function confirmTwoFactor(codigo: string): Promise<RecoveryCodes> {
  return post<RecoveryCodes>('/account/two-factor/confirm', { codigo });
}

/** `200` 10 novos; `401` código; `409` não ligada; `423`; `500` a chave do TOTP trocada. */
export function regenerateRecoveryCodes(codigo: string): Promise<RecoveryCodes> {
  return post<RecoveryCodes>('/account/two-factor/recovery-codes', { codigo });
}

/** `204`; `401` código; `409` não ligada; `423`; `500` a chave do TOTP trocada. */
export function removeTwoFactor(codigo: string): Promise<void> {
  return post<void>('/account/two-factor/remove', { codigo });
}

// --- Sessões (#219) ---

export interface AccountSession {
  id: string;
  criadaEm: string;
  ultimoUso: string;
  atual: boolean;
}

export interface EndedSessions {
  encerradas: number;
}

/** Só as vivas, por `criadaEm` decrescente (`UserSessions.ListLiveAsync`). */
export function listSessions(): Promise<AccountSession[]> {
  return request<AccountSession[]>('/account/sessions');
}

/** `204`; `404` a sessão já tinha saído. */
export function revokeSession(id: string): Promise<void> {
  return post<void>(`/account/sessions/${encodeURIComponent(id)}/revoke`);
}

export function revokeOtherSessions(): Promise<EndedSessions> {
  return post<EndedSessions>('/account/sessions/revoke-others');
}
