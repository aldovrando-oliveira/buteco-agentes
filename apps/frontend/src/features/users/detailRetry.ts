import type { QueryClient } from '@tanstack/react-query';
import { isApiFailure } from '../../utils/failureNotice';

// O `retry` da consulta do detalhe de Editar usuário (requisito "Página Editar usuário em
// /users/:id"; design.md da change editar-usuario-painel, D3).

export const is404 = (error: unknown) => isApiFailure(error) && error.status === 404;

/**
 * O `retry` do detalhe: nunca repete o `404` (o usuário não existe, e a espera exponencial do
 * padrão o deixaria uns 7 s em "Carregando…"); no resto, a política do QueryClient do painel.
 */
export function detailRetry(queryClient: QueryClient) {
  const padrao = queryClient.getDefaultOptions().queries?.retry;
  return (failureCount: number, error: unknown): boolean => {
    if (is404(error)) return false;
    if (typeof padrao === 'function') return padrao(failureCount, error as Error);
    if (typeof padrao === 'number') return failureCount < padrao;
    return padrao ?? failureCount < 3;
  };
}
