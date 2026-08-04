import type { Agent, AgentMcpServerBinding, CreateAgentInput, UpdateAgentInput } from '../types/agent';

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

// Default alinhado ao profile "http" do launchSettings.json de apps/api — sem
// isso, um `npm run dev` sem `.env` local monta a URL como "undefined/agents"
// (import.meta.env.VITE_API_BASE_URL fica undefined), que o browser resolve
// como caminho relativo em vez de falhar de forma clara.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5017';

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  });

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

export function listAgents(): Promise<Agent[]> {
  return request<Agent[]>('/agents');
}

export function getAgent(id: string): Promise<Agent> {
  return request<Agent>(`/agents/${id}`);
}

export function createAgent(input: CreateAgentInput): Promise<Agent> {
  return request<Agent>('/agents', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export function updateAgent(id: string, input: UpdateAgentInput): Promise<Agent> {
  return request<Agent>(`/agents/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export function activateAgent(id: string): Promise<Agent> {
  return request<Agent>(`/agents/${id}/activate`, { method: 'POST' });
}

export function deactivateAgent(id: string): Promise<Agent> {
  return request<Agent>(`/agents/${id}/deactivate`, { method: 'POST' });
}

export function replaceAgentMcpServers(
  agentId: string,
  bindings: AgentMcpServerBinding[],
): Promise<Agent> {
  return request<Agent>(`/agents/${agentId}/mcp-servers`, {
    method: 'PUT',
    body: JSON.stringify({ mcpServers: bindings }),
  });
}
