import { QueryClient } from '@tanstack/react-query';
import { queryRetry, queryRetryDelay } from '../auth/transient';

// Consulta que falha com `403` não repete (design.md da change papeis-no-painel, D3).
// Consulta que falha com `503` repete até 3 vezes esperando o `Retry-After` (de 1 a
// 30 s), e os outros erros mantêm o padrão do TanStack Query: as mesmas 3 tentativas,
// com espera exponencial. Mutação não repete, como no padrão; o `503` de contenção do
// apps/api já tem texto de "repita" nas telas (design.md da change
// painel-entra-pelo-emissor, D4, decisão do dono P4). Consulta que declara `retry`
// próprio continua com ele.
export function createAppQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: (failureCount, error) => queryRetry(failureCount, error),
        retryDelay: queryRetryDelay,
      },
    },
  });
}

export const queryClient = createAppQueryClient();
