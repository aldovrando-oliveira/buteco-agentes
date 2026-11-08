import { sessionFetch } from '../../../auth/session';
import { retryAfterSeconds } from '../../../auth/transient';

// `request<T>` E `ApiError` PRÓPRIOS DA FEATURE, copiados do idioma de `accountApi.ts`. Não é
// descuido nem duplicação a corrigir: é regra escrita da casa (convenção 2 — esperar 2-3
// consumidores REAIS antes de extrair). Extrair um cliente compartilhado é change própria,
// com todas as cópias na mesa — não efeito colateral de uma tela nova. Os avisos de falha
// (`utils/failureNotice.ts`) reconhecem este erro pelos campos, e não pela classe (decisão
// do dono Q2 da change usuarios-convites-identidade-painel).
//
// As rotas são as de Usuários no apps/api: `GET /users` (#219) e o convite (#181). O `401`
// "Senha inválida." da senha de quem chama chega aqui como `ApiError` com o `title` do
// servidor, sem deslogar: o `sessionFetch` o devolve sem renovar (design.md da change
// minha-conta-painel, D10).

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

/** O vencimento do convite; `vencido` vem do relógio do servidor (`UserSummary`). */
export interface InvitationSummary {
  expiraEm: string;
  vencido: boolean;
}

/** Uma linha de `GET /users` (#219, #181). */
export interface UserSummary {
  id: string;
  nome: string;
  email: string;
  /** `ativo`, `desativado` ou `convidado`; outro valor sai neutro na tela (convenção 13). */
  estado: string;
  formasDeEntrada: string[];
  duasEtapas: boolean;
  ultimoAcesso: string | null;
  criadoEm: string;
  convite: InvitationSummary | null;
}

/** O link de uso único, mostrado uma vez: nunca vai para cache, storage ou URL (D5). */
export interface AccessLink {
  link: string;
  expiraEm: string;
}

export interface InvitationCreated extends AccessLink {
  usuario: UserSummary;
}

export interface InvitationRequest {
  nome: string;
  email: string;
  senha: string;
}

export function listUsers(): Promise<UserSummary[]> {
  return request<UserSummary[]>('/users');
}

/** `201`; `400` campos; `409` e-mail em uso; `401` "Senha inválida."; `423` + `Retry-After`. */
export function inviteUser(body: InvitationRequest): Promise<InvitationCreated> {
  return post<InvitationCreated>('/users/invitations', body);
}

/** `200`; `404`; `409` não é convite; `401`; `423`. */
export function renewInvitation(id: string, senha: string): Promise<AccessLink> {
  return post<AccessLink>(`/users/${encodeURIComponent(id)}/invitation/renew`, { senha });
}

/** `204`; `404`; `409` "O convite já foi aceito.". Não pede senha: só tira acesso. */
export function cancelInvitation(id: string): Promise<void> {
  return post<void>(`/users/${encodeURIComponent(id)}/invitation/cancel`);
}
