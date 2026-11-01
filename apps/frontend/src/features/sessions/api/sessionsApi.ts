import type {
  ChannelSession,
  MessagesSummary,
  SessionMessage,
  SessionsSummary,
} from '../types/session';
import { sessionFetch } from '../../../auth/session';
import { retryAfterSeconds } from '../../../auth/transient';

export interface ValidationProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ValidationProblemDetails;
  /** O `Retry-After` do `503`, em segundos, para o QueryClient esperar (auth/transient.ts). */
  readonly retryAfter?: number;

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
  // O token, a renovação e o `401` são do `sessionFetch` (design.md da change
  // painel-entra-pelo-emissor, D5 e D13); a URL, o erro e o corpo continuam daqui.
  const response = await sessionFetch(`${INBOX_BASE_URL}${path}`, {
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
