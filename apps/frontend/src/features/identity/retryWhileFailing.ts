import { MAX_RETRY_AFTER_SECONDS, MIN_RETRY_AFTER_SECONDS } from '../../auth/transient';
import { isApiFailure } from '../../utils/failureNotice';

// "O painel tenta de novo sozinho" (requisito "Identidade indisponível e nova tentativa
// automática"; decisão do dono Q9): polling CONDICIONAL, com a condição em função pura
// (convenção 20). Só enquanto a consulta está em erro: no `503`, o `Retry-After` preso entre
// 1 e 30 s como no `queryRetryDelay`; em outro erro, 30 s. Com dado e sem erro, `false`.

export const INTERVALO_SEM_RETRY_AFTER_MS = 30_000;

interface QueryLike {
  state: { status: 'pending' | 'error' | 'success'; error: unknown };
}

export function retryWhileFailing(query: QueryLike): number | false {
  if (query.state.status !== 'error') return false;
  const error = query.state.error;
  if (isApiFailure(error) && error.status === 503 && error.retryAfter !== undefined) {
    const segundos = Math.min(MAX_RETRY_AFTER_SECONDS, Math.max(MIN_RETRY_AFTER_SECONDS, error.retryAfter));
    return segundos * 1000;
  }
  return INTERVALO_SEM_RETRY_AFTER_MS;
}
