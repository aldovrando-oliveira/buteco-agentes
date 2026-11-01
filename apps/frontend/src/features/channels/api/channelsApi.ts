import type { Channel, CreateChannelInput, UpdateChannelInput } from '../types/channel';
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

// Default alinhado ao profile "http" do launchSettings.json de apps/inbox —
// porta 5027, diferente da porta 5017 de apps/api já usada por
// agentsApi.ts/mcpServersApi.ts (design.md, Decision 6).
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

export function listChannels(): Promise<Channel[]> {
  return request<Channel[]>('/channels');
}

export function getChannel(id: string): Promise<Channel> {
  return request<Channel>(`/channels/${id}`);
}

export function createChannel(input: CreateChannelInput): Promise<Channel> {
  return request<Channel>('/channels', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export function updateChannel(id: string, input: UpdateChannelInput): Promise<Channel> {
  return request<Channel>(`/channels/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export function activateChannel(id: string): Promise<Channel> {
  return request<Channel>(`/channels/${id}/activate`, { method: 'POST' });
}

export function deactivateChannel(id: string): Promise<Channel> {
  return request<Channel>(`/channels/${id}/deactivate`, { method: 'POST' });
}
