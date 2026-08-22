import type { ChannelSession, SessionMessage } from '../types/session';
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
