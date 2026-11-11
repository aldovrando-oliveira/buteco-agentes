import { sessionFetch } from '../../../auth/session';
import { retryAfterSeconds } from '../../../auth/transient';
import type {
  AgentApplication,
  AgentApplicationsCount,
  ApplicationAgent,
  ApplicationDetail,
  ApplicationSummary,
  CreatedApplication,
  CreatedSecret,
  EstadoFiltro,
  Operacao,
} from '../types';

// `request<T>` E `ApiError` PRÓPRIOS DA FEATURE, no idioma de `usersApi.ts` (convenção 2: extrair
// um cliente comum é change própria, com todas as cópias na mesa). Os avisos de falha
// (`utils/failureNotice.ts`) reconhecem o erro pelos campos, não pela classe.
//
// As rotas são as da #186 (`/applications/*`) e da #187 (`/applications/{id}/agents`,
// `/agents/{id}/applications*`). O `401` "Senha inválida." chega aqui com o `title`, sem deslogar:
// o `sessionFetch` não renova o `401` com `problem+json`.
//
// AS DUAS CHAMADAS QUE DEVOLVEM SEGREDO (`createApplication` e `generateSecret`) são chamadas
// direto pelo componente, nunca por `useMutation`, e a resposta nunca vai a `setQueryData`: o
// segredo vive só no estado de quem chamou (spec `panel-external-applications`, "O segredo vive só
// no componente que o recebeu").

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

  constructor(
    status: number,
    message: string,
    problem?: ValidationProblemDetails,
    retryAfter?: number,
  ) {
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
    const problem = (await response.json().catch(() => undefined)) as
      ValidationProblemDetails | undefined;
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

const send = <T>(method: string, path: string, body?: unknown) =>
  request<T>(path, { method, body: body === undefined ? undefined : JSON.stringify(body) });

/** Os campos de erro de um `400` `ValidationProblem`, o primeiro texto de cada um. */
export function fieldErrors(error: unknown): Record<string, string> {
  if (!(error instanceof ApiError) || error.status !== 400) return {};
  return Object.fromEntries(
    Object.entries(error.problem?.errors ?? {}).map(([campo, textos]) => [campo, textos[0]]),
  );
}

// ---------------------------------------------------------------- aplicações (#186)

export interface ListFilter {
  busca?: string;
  estado?: EstadoFiltro;
}

/** `GET /applications/`, com `busca` aparada e `estado` só quando não é "todas". */
export function listApplications(filter: ListFilter = {}): Promise<ApplicationSummary[]> {
  const query = new URLSearchParams();
  const busca = filter.busca?.trim();
  if (busca) query.set('busca', busca);
  if (filter.estado && filter.estado !== 'todas') query.set('estado', filter.estado);
  const qs = query.toString();
  return request<ApplicationSummary[]>(`/applications/${qs ? `?${qs}` : ''}`);
}

export function getApplication(id: string): Promise<ApplicationDetail> {
  return request<ApplicationDetail>(`/applications/${encodeURIComponent(id)}`);
}

export interface ApplicationInput {
  nome: string;
  /** Vazia vai como `null`: o servidor a grava como ausente de qualquer jeito. */
  descricao: string | null;
}

export function createApplication(
  input: ApplicationInput & { senha: string },
): Promise<CreatedApplication> {
  return send<CreatedApplication>('POST', '/applications/', input);
}

export function updateApplication(id: string, input: ApplicationInput): Promise<ApplicationDetail> {
  return send<ApplicationDetail>('PUT', `/applications/${encodeURIComponent(id)}`, input);
}

export function deactivateApplication(id: string): Promise<ApplicationDetail> {
  return send<ApplicationDetail>('POST', `/applications/${encodeURIComponent(id)}/deactivate`);
}

export function activateApplication(id: string): Promise<ApplicationDetail> {
  return send<ApplicationDetail>('POST', `/applications/${encodeURIComponent(id)}/activate`);
}

export function generateSecret(id: string, senha: string): Promise<CreatedSecret> {
  return send<CreatedSecret>('POST', `/applications/${encodeURIComponent(id)}/secrets`, { senha });
}

export function revokeSecret(id: string, secretId: string): Promise<ApplicationDetail> {
  return send<ApplicationDetail>(
    'POST',
    `/applications/${encodeURIComponent(id)}/secrets/${encodeURIComponent(secretId)}/revoke`,
  );
}

// ---------------------------------------------------------------- vínculos (#187)

export function listApplicationAgents(id: string): Promise<ApplicationAgent[]> {
  return request<ApplicationAgent[]>(`/applications/${encodeURIComponent(id)}/agents`);
}

export function grantAgent(
  id: string,
  agenteId: string,
  operacoes: Operacao[],
): Promise<ApplicationAgent> {
  return send<ApplicationAgent>('POST', `/applications/${encodeURIComponent(id)}/agents`, {
    agenteId,
    operacoes,
  });
}

export function updateAgentOperations(
  id: string,
  agenteId: string,
  operacoes: Operacao[],
): Promise<ApplicationAgent> {
  return send<ApplicationAgent>(
    'PUT',
    `/applications/${encodeURIComponent(id)}/agents/${encodeURIComponent(agenteId)}`,
    { operacoes },
  );
}

export function revokeAgent(id: string, agenteId: string): Promise<void> {
  return send<void>(
    'DELETE',
    `/applications/${encodeURIComponent(id)}/agents/${encodeURIComponent(agenteId)}`,
  );
}

/** Só do Admin: a lista com nomes. Operador e Leitor recebem `403` (#187, Q8). */
export function listAgentApplications(agentId: string): Promise<AgentApplication[]> {
  return request<AgentApplication[]>(`/agents/${encodeURIComponent(agentId)}/applications`);
}

/** Os três papéis: só `{ total, ativas }`, sem nome nem identificador. */
export function countAgentApplications(agentId: string): Promise<AgentApplicationsCount> {
  return request<AgentApplicationsCount>(
    `/agents/${encodeURIComponent(agentId)}/applications/count`,
  );
}
