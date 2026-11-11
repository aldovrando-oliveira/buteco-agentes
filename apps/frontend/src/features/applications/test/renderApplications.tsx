import { render } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Link, RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { ApplicationsListPage } from '../pages/ApplicationsListPage';
import { ApplicationCreatePage } from '../pages/ApplicationCreatePage';
import { ApplicationDetailPage } from '../pages/ApplicationDetailPage';
import { ApplicationEditPage } from '../pages/ApplicationEditPage';

/**
 * As quatro páginas de aplicações num router de memória, com uma página "fora" para sair da tela
 * e o QueryClient de teste. Devolve o router, para o caso ler a `location.state` e o histórico (os
 * guardas do segredo), e o QueryClient, para ler os dois caches.
 */
export function renderApplications(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/applications', element: <ApplicationsListPage /> },
      { path: '/applications/new', element: <ApplicationCreatePage /> },
      { path: '/applications/:id', element: <ApplicationDetailPage /> },
      { path: '/applications/:id/edit', element: <ApplicationEditPage /> },
      {
        path: '/users',
        element: (
          <>
            <p>fora de Aplicações</p>
            <Link to="/applications">voltar</Link>
          </>
        ),
      },
      { path: '/agents/:id', element: <p>detalhe do agente</p> },
    ],
    { initialEntries: [path] },
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

/** Tudo o que os dois caches do TanStack Query guardam, serializado, para procurar o segredo. */
export function cachesSerializados(queryClient: QueryClient): string {
  const consultas = queryClient
    .getQueryCache()
    .getAll()
    .map((q) => q.state);
  const mutacoes = queryClient
    .getMutationCache()
    .getAll()
    .map((m) => m.state);
  return JSON.stringify({ consultas, mutacoes });
}
