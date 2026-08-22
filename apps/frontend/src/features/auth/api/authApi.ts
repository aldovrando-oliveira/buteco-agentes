import type { LoginInput, LoginResult } from '../types/auth';

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

// Sem token anexado de propósito — não há sessão antes do login (design.md,
// Decision 7). Diferente do request<T> das outras features, este não
// importa src/auth/token.ts.
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

  return (await response.json()) as T;
}

export function login(input: LoginInput): Promise<LoginResult> {
  return request<LoginResult>('/auth/login', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}
