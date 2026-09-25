import type { SystemInsights } from '../types/systemInsights';
import { clearToken, getToken } from '../../../auth/token';

// `request<T>` E `ApiError` PRÓPRIOS DA FEATURE, copiados do idioma de
// `agentsApi.ts`. Não é descuido nem duplicação a corrigir: é regra escrita da
// casa (convenção 2 — esperar 2-3 consumidores REAIS antes de extrair), e o
// painel tem cinco cópias deste bloco hoje (`agentsApi`, `sessionsApi`,
// `channelsApi`, `mcpServersApi`, `knowledgeBasesApi`). Extrair um cliente
// compartilhado é change própria, com as cinco na mesa — não efeito colateral
// de uma tela nova.
//
// O que NÃO é copiado é o token: `src/auth/token.ts` é um módulo fino, sem
// fetch, e cada `request<T>` o importa. Ele não é um cliente HTTP.

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
// isso, um `npm run dev` sem `.env` local monta a URL como "undefined/insights/system".
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

  return (await response.json()) as T;
}

// OS DOIS LIMITES SÃO OBRIGATÓRIOS. `InsightsPeriod.MissingBoundMessage` faz a
// rota responder 400 sem eles — não existe período implícito, e a janela é
// responsabilidade de quem pergunta. Mesmo idioma do `periodQuery` de
// `sessionsApi.ts`.
function periodQuery(from: string, to: string): string {
  return new URLSearchParams({ from, to }).toString();
}

export function getSystemInsights(from: string, to: string): Promise<SystemInsights> {
  return request<SystemInsights>(`/insights/system?${periodQuery(from, to)}`);
}
