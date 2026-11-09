// `503` com `Retry-After` é indisponibilidade passageira, e nunca desloga (design.md da
// change painel-entra-pelo-emissor, D4). O `request<T>` de cada feature lê o cabeçalho
// para o erro dela, e o QueryClient repete a consulta esperando esse tempo.

/** O `Retry-After` em segundos: o número, ou a data HTTP menos agora. */
export function retryAfterSeconds(response: Response, now = Date.now()): number | undefined {
  const header = response.headers.get('Retry-After');
  if (header === null || header.trim() === '') return undefined;
  if (/^\d+$/.test(header.trim())) return Number(header.trim());
  const date = Date.parse(header);
  return Number.isNaN(date) ? undefined : Math.max(0, Math.ceil((date - now) / 1000));
}

export const MIN_RETRY_AFTER_SECONDS = 1;
export const MAX_RETRY_AFTER_SECONDS = 30;

interface UnavailableError {
  status: 503;
  retryAfter?: number;
}

export function isUnavailableError(error: unknown): error is UnavailableError {
  return (error as { status?: unknown } | null)?.status === 503;
}

// O padrão do TanStack Query, repetido para os erros que não são 503: 3 tentativas
// (`retryer.js`, `config.retry ?? 3`) e espera exponencial até 30 s
// (`defaultRetryDelay`).
const DEFAULT_RETRIES = 3;
const defaultRetryDelay = (failureCount: number) => Math.min(1000 * 2 ** failureCount, 30_000);

/**
 * O `403` não se repete (spec `panel-issuer-session`; design.md da change papeis-no-painel, D3):
 * o papel não muda entre as tentativas, e a espera exponencial deixaria a tela uns 7 s em
 * "Carregando…" antes do acesso negado. A renovação do `sessionFetch` já buscou o papel novo.
 */
export function queryRetry(failureCount: number, error?: unknown): boolean {
  if ((error as { status?: unknown } | null)?.status === 403) return false;
  return failureCount < DEFAULT_RETRIES;
}

export function queryRetryDelay(failureCount: number, error: unknown): number {
  if (!isUnavailableError(error)) return defaultRetryDelay(failureCount);
  const seconds = Math.min(
    MAX_RETRY_AFTER_SECONDS,
    Math.max(MIN_RETRY_AFTER_SECONDS, error.retryAfter ?? MIN_RETRY_AFTER_SECONDS),
  );
  return seconds * 1000;
}
