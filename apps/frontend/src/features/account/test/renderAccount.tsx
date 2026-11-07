import type { ReactNode } from 'react';
import { render } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router';
import { theme } from '../../../theme';

/** Um cartão ou a página de Minha conta, com o QueryClient de teste e o router. */
export function renderAccount(ui: ReactNode, path = '/account') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const result = render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/account" element={ui} />
            <Route path="*" element={<p>fora de Minha conta</p>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
  return { ...result, queryClient };
}
