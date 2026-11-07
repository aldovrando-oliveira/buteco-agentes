import { render } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Link, MemoryRouter, Route, Routes } from 'react-router';
import { theme } from '../../../theme';
import { UsersPage } from '../pages/UsersPage';

/** A página Usuários com o QueryClient de teste e uma rota de fora para "sair da tela". */
export function renderUsers() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const result = render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/users']}>
          <Routes>
            <Route
              path="/users"
              element={
                <>
                  <Link to="/inventory">sair</Link>
                  <UsersPage />
                </>
              }
            />
            <Route
              path="/inventory"
              element={
                <>
                  <p>fora de Usuários</p>
                  <Link to="/users">voltar</Link>
                </>
              }
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
  return { ...result, queryClient };
}
