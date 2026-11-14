import { sessionFetch } from '../../../auth/session';
import { retryAfterSeconds } from '../../../auth/transient';
import type { ConnectedAccount } from '../../account/api/accountApi';

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

/**
 * A confirmação de quem chama (#189, `CallerPassword.RejectAsync`): a senha, para quem tem senha, ou
 * o código do app autenticador, para quem não tem. Vai UM campo só no corpo; o backend confere o do
 * modo e ignora o outro (change telas-contas-federadas, D2).
 */
export type CallerConfirmation = { senha: string } | { codigo: string };

/** Uma linha de `GET /users` (#219, #181). */
export interface UserSummary {
  id: string;
  nome: string;
  email: string;
  /** `ativo`, `desativado` ou `convidado`; outro valor sai neutro na tela (convenção 13). */
  estado: string;
  /**
   * `admin`, `operador` ou `leitor` (#184), também no convidado; lido pelo `papelDaResposta`
   * de `auth/roles.ts`, e outro valor sai neutro na tela (change papeis-no-painel).
   */
  papel: string;
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
  /** Obrigatório desde a #184: `admin`, `operador` ou `leitor` (`papelParaCorpo`). */
  papel: string;
}

export function listUsers(): Promise<UserSummary[]> {
  return request<UserSummary[]>('/users');
}

/** `201`; `400` campos (inclusive `papel`); `409` e-mail em uso; `401` "Senha inválida."; `423` + `Retry-After`. */
export function inviteUser(body: InvitationRequest & CallerConfirmation): Promise<InvitationCreated> {
  return post<InvitationCreated>('/users/invitations', body);
}

/** `200`; `404`; `409` não é convite; `401`; `423`. */
export function renewInvitation(id: string, confirmacao: CallerConfirmation): Promise<AccessLink> {
  return post<AccessLink>(`/users/${encodeURIComponent(id)}/invitation/renew`, confirmacao);
}

/** `204`; `404`; `409` "O convite já foi aceito.". Não pede senha: só tira acesso. */
export function cancelInvitation(id: string): Promise<void> {
  return post<void>(`/users/${encodeURIComponent(id)}/invitation/cancel`);
}

// O detalhe e as ações de Editar usuário (#219, #181, #218; design.md da change
// editar-usuario-painel, D3 a D10).

/** Quem convidou: o primeiro Admin (bootstrap) não tem (#181). */
export interface UserReference {
  id: string;
  nome: string;
}

/** `GET /users/{id}`: o resumo + a desativação, quem convidou e as sessões (#219, #181). */
export interface UserDetail extends UserSummary {
  desativadoEm: string | null;
  convidadoPor: UserReference | null;
  sessoes: { ativas: number; ultimaAtividade: string | null };
  /** As contas de provedores da pessoa (#189), como em `GET /account`. */
  contasConectadas: ConnectedAccount[];
}

const segment = (id: string) => `/users/${encodeURIComponent(id)}`;

/** `200`; `404`. */
export function getUser(id: string): Promise<UserDetail> {
  return request<UserDetail>(segment(id));
}

/** `200` detalhe; `400` campos; `404`; `409` "Já existe um usuário com este e-mail.". */
export function updateUser(id: string, body: { nome: string; email: string }): Promise<UserDetail> {
  return request<UserDetail>(segment(id), { method: 'PUT', body: JSON.stringify(body) });
}

/** `200` detalhe; `403` a si mesmo; `404`; `409` convidado. Idempotente. */
export function deactivateUser(id: string): Promise<UserDetail> {
  return post<UserDetail>(`${segment(id)}/deactivate`);
}

/** `200` detalhe; `404`; `409` convidado. Idempotente. */
export function activateUser(id: string): Promise<UserDetail> {
  return post<UserDetail>(`${segment(id)}/activate`);
}

/** `200 { encerradas }`; `404`. Não pede senha: só tira acesso. */
export function revokeUserSessions(id: string): Promise<{ encerradas: number }> {
  return post<{ encerradas: number }>(`${segment(id)}/sessions/revoke`);
}

/**
 * O link de redefinição, mostrado uma vez (D7): nunca vai para cache, storage ou URL.
 * `200`; `403` a si mesmo; `404`; `409` convidado ou desativado; `401` "Senha inválida."; `423`.
 */
export function requestPasswordReset(id: string, confirmacao: CallerConfirmation): Promise<AccessLink> {
  return post<AccessLink>(`${segment(id)}/password-reset`, confirmacao);
}

/** `204`; `403` a si mesmo; `404`; `409` já não configurada. Encerra as sessões da pessoa. */
export function resetTwoFactor(id: string): Promise<void> {
  return post<void>(`${segment(id)}/two-factor/reset`);
}

/**
 * `PUT /users/{id}/role` (#184; change papeis-no-painel, D4). A `senha` de quem chama vai só na
 * promoção a Admin; rebaixar não a pede, e o corpo não a leva. `200` detalhe; `400`
 * `errors.papel`; `401` "Senha inválida."; `423`; `403`; `404`; `409` o último Admin ou o
 * desativado.
 */
export function changeUserRole(
  id: string,
  body: { papel: string } | ({ papel: string } & CallerConfirmation),
): Promise<UserDetail> {
  return request<UserDetail>(`${segment(id)}/role`, { method: 'PUT', body: JSON.stringify(body) });
}

/**
 * O Admin desconecta a conta de provedor de OUTRA pessoa (#189). `204`; `403` "Para desconectar a sua
 * própria conta, use Minha conta."; `404` "Usuário não encontrado." ou "Esta conta não está conectada a
 * esse provedor."; `409` a última forma. Sem corpo e sem senha. O backend troca o stamp da pessoa e
 * encerra as sessões dela (change telas-contas-federadas, D6).
 */
export function disconnectUserFederated(id: string, chave: string): Promise<void> {
  return request<void>(`${segment(id)}/federated/${encodeURIComponent(chave)}`, { method: 'DELETE' });
}
