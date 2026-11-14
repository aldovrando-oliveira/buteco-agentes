import { render } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { theme } from '../../../theme';
import { AccountPage } from '../pages/AccountPage';

/**
 * A página de Minha conta num router de memória que o caso lê: a volta do conectar troca o endereço
 * por `replace`, e o caso confere o `search` e o `state` depois (change telas-contas-federadas, D4).
 */
export function renderAccountPage(
  entry: string | { pathname: string; search?: string; state?: unknown },
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/account', element: <AccountPage /> },
      { path: '*', element: <p>fora de Minha conta</p> },
    ],
    { initialEntries: [entry] },
  );
  const result = render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );
  return { ...result, router, queryClient };
}
