import { sessionFetch } from '../../../auth/session';
import { retryAfterSeconds } from '../../../auth/transient';

// `request<T>` E `ApiError` PRÓPRIOS DA FEATURE, copiados do idioma de `accountApi.ts` (convenção
// 2 — esperar 2-3 consumidores REAIS antes de extrair; extrair é change própria). A rota é a
// leitura do emissor e das chaves (#211, `GET /identity`). Com o anel de chaves fora, o gate
// do apps/api responde `503` com `Retry-After` antes dela.

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

  constructor(status: number, message: string, problem?: ValidationProblemDetails, retryAfter?: number) {
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
    const problem = (await response.json().catch(() => undefined)) as ValidationProblemDetails | undefined;
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

export interface IssuerOverview {
  issuer: string;
  discovery: string;
  jwks: string;
  token: string;
  validadeDoTokenSegundos: number;
  sessaoSemAtividadeSegundos: number;
}

export interface SigningKey {
  kid: string;
  /** `em-uso`, `proxima` ou `aposentando`; outro valor sai neutro na tela (convenção 13). */
  estado: string;
  criadaEm: string;
  assinaDesde: string;
  assinaAte: string;
  saiDaPublicacaoEm: string;
}

export interface SigningKeys {
  intervaloDeTrocaSegundos: number;
  prePublicacaoSegundos: number;
  retencaoSegundos: number;
  verificacaoSegundos: number;
  chaves: SigningKey[];
}

export interface IdentityOverview {
  emissor: IssuerOverview;
  chavesDeAssinatura: SigningKeys;
}

export const IDENTITY_QUERY_KEY = ['identity'] as const;

export function getIdentity(): Promise<IdentityOverview> {
  return request<IdentityOverview>('/identity');
}
