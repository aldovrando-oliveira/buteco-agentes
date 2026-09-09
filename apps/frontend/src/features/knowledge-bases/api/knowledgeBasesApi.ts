import type {
  CreateKnowledgeBaseInput,
  KnowledgeBase,
  UpdateKnowledgeBaseInput,
} from '../types/knowledgeBase';
import { clearToken, getToken } from '../../../auth/token';

// request<T> e ApiError próprios da feature, e não importados de um cliente
// comum: a convenção 7 é explícita em que não há cliente HTTP compartilhado
// entre features, e a preocupação transversal (token) entra como o módulo fino
// que este request importa. O handoff sugeria extrair para src/api/httpClient.ts
// — recusado, com o motivo em design.md, D12.
export interface ValidationProblemDetails {
  title?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ValidationProblemDetails;

  constructor(status: number, message: string, problem?: ValidationProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5017';

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getToken();
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  });

  if (response.status === 401) {
    clearToken();
    window.location.href = '/login';
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as
      | ValidationProblemDetails
      | undefined;
    throw new ApiError(
      response.status,
      problem?.title ?? `Erro ${response.status} ao chamar ${path}`,
      problem,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

// As seis rotas que apps/api expõe. Não há DELETE: base é entidade de catálogo
// e segue o padrão IsActive de McpServer.
export function listKnowledgeBases(): Promise<KnowledgeBase[]> {
  return request<KnowledgeBase[]>('/knowledge-bases');
}

export function getKnowledgeBase(id: string): Promise<KnowledgeBase> {
  return request<KnowledgeBase>(`/knowledge-bases/${id}`);
}

export function createKnowledgeBase(input: CreateKnowledgeBaseInput): Promise<KnowledgeBase> {
  return request<KnowledgeBase>('/knowledge-bases', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export function updateKnowledgeBase(
  id: string,
  input: UpdateKnowledgeBaseInput,
): Promise<KnowledgeBase> {
  return request<KnowledgeBase>(`/knowledge-bases/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export function activateKnowledgeBase(id: string): Promise<KnowledgeBase> {
  return request<KnowledgeBase>(`/knowledge-bases/${id}/activate`, { method: 'POST' });
}

export function deactivateKnowledgeBase(id: string): Promise<KnowledgeBase> {
  return request<KnowledgeBase>(`/knowledge-bases/${id}/deactivate`, { method: 'POST' });
}
