import type {
  ChannelSession,
  MessagesSummary,
  SessionMessage,
  SessionsSummary,
} from '../types/session';
import { clearToken, getToken } from '../../../auth/token';

export interface ValidationProblemDetails {
  title?: string;
  detail?: string;
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

// Mesmo padrão de duplicação deliberada de request<T>/ApiError já usado por
// channelsApi.ts/agentsApi.ts/mcpServersApi.ts (design.md, Decisão 1).
//
// ESTE MÓDULO É O CLIENTE DE CONVERSAS DE apps/inbox, NÃO O CLIENTE DO RECURSO
// `/sessions`. Ele já falava com `/channels/{id}/sessions` e com
// `/sessions/{id}/messages`, e por isso `/messages/summary` mora aqui também, em
// vez de num `messagesApi.ts` que seria a sétima cópia de request<T>/ApiError
// para uma função só, numa pasta `features/messages/` sem tela nenhuma.
// Gatilho para separar: surgir uma tela própria de mensagens
// (frontend-inventario-atividade-periodo, design.md, D4).
const INBOX_BASE_URL = import.meta.env.VITE_INBOX_BASE_URL ?? 'http://localhost:5027';

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getToken();
  const response = await fetch(`${INBOX_BASE_URL}${path}`, {
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
      ValidationProblemDetails | undefined;
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

export function listChannelSessions(channelId: string): Promise<ChannelSession[]> {
  return request<ChannelSession[]>(`/channels/${channelId}/sessions`);
}

export function getSessionMessages(sessionId: string): Promise<SessionMessage[]> {
  return request<SessionMessage[]>(`/sessions/${sessionId}/messages`);
}

// Os dois limites são obrigatórios na rota e não há período implícito: quem
// chama decide a janela (features/inventory/utils/activityWindow.ts). Ambos são
// inclusivos no backend.
function periodQuery(from: string, to: string): string {
  return new URLSearchParams({ from, to }).toString();
}

export function getSessionsSummary(from: string, to: string): Promise<SessionsSummary> {
  return request<SessionsSummary>(`/sessions/summary?${periodQuery(from, to)}`);
}

export function getMessagesSummary(from: string, to: string): Promise<MessagesSummary> {
  return request<MessagesSummary>(`/messages/summary?${periodQuery(from, to)}`);
}
