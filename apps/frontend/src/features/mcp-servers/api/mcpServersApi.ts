import type {
  CreateMcpServerInput,
  McpConnectionTestResult,
  McpServer,
  McpServerToolsResult,
  TestMcpServerConfigInput,
  UpdateMcpServerInput,
} from '../types/mcpServer';
import { sessionFetch } from '../../../auth/session';
import { retryAfterSeconds } from '../../../auth/transient';

export interface ValidationProblemDetails {
  title?: string;
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

export function listMcpServers(): Promise<McpServer[]> {
  return request<McpServer[]>('/mcp-servers');
}

export function getMcpServer(id: string): Promise<McpServer> {
  return request<McpServer>(`/mcp-servers/${id}`);
}

export function createMcpServer(input: CreateMcpServerInput): Promise<McpServer> {
  return request<McpServer>('/mcp-servers', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export function updateMcpServer(id: string, input: UpdateMcpServerInput): Promise<McpServer> {
  return request<McpServer>(`/mcp-servers/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export function activateMcpServer(id: string): Promise<McpServer> {
  return request<McpServer>(`/mcp-servers/${id}/activate`, { method: 'POST' });
}

export function deactivateMcpServer(id: string): Promise<McpServer> {
  return request<McpServer>(`/mcp-servers/${id}/deactivate`, { method: 'POST' });
}

export function testUnsavedMcpServerConnection(
  input: TestMcpServerConfigInput,
): Promise<McpConnectionTestResult> {
  return request<McpConnectionTestResult>('/mcp-servers/test', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export function testSavedMcpServerConnection(id: string): Promise<McpConnectionTestResult> {
  return request<McpConnectionTestResult>(`/mcp-servers/${id}/test`, { method: 'POST' });
}

export function listMcpServerTools(id: string): Promise<McpServerToolsResult> {
  return request<McpServerToolsResult>(`/mcp-servers/${id}/tools`);
}
