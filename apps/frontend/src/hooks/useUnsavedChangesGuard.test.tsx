import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { Link, RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../theme';
import { useUnsavedChangesGuard } from './useUnsavedChangesGuard';

function Harness({ dirty }: { dirty: boolean }) {
  const guard = useUnsavedChangesGuard(dirty);

  return (
    <>
      <p>{guard.isBlocked ? 'navegação bloqueada' : 'navegação livre'}</p>
      <Link to="/outra">ir para outra rota</Link>
      <Link to="/?tab=ferramentas">trocar de aba</Link>
      <button type="button" onClick={guard.confirmNavigation}>
        sair mesmo assim
      </button>
      <button type="button" onClick={guard.cancelNavigation}>
        continuar editando
      </button>
    </>
  );
}

function renderHarness(dirty: boolean) {
  const router = createMemoryRouter(
    [
      { path: '/', element: <Harness dirty={dirty} /> },
      { path: '/outra', element: <p>outra rota</p> },
    ],
    { initialEntries: ['/'] },
  );

  return render(
    <MantineProvider theme={theme}>
      <RouterProvider router={router} />
    </MantineProvider>,
  );
}

describe('useUnsavedChangesGuard', () => {
  it('sem rascunho, a navegação acontece direto', async () => {
    const user = userEvent.setup();
    renderHarness(false);

    await user.click(screen.getByRole('link', { name: 'ir para outra rota' }));

    expect(await screen.findByText('outra rota')).toBeInTheDocument();
  });

  it('com rascunho, bloqueia a saída da rota e mantém a tela atual', async () => {
    const user = userEvent.setup();
    renderHarness(true);

    await user.click(screen.getByRole('link', { name: 'ir para outra rota' }));

    expect(await screen.findByText('navegação bloqueada')).toBeInTheDocument();
    expect(screen.queryByText('outra rota')).not.toBeInTheDocument();
  });

  it('com rascunho, bloqueia também uma navegação que muda apenas a query string', async () => {
    const user = userEvent.setup();
    renderHarness(true);

    await user.click(screen.getByRole('link', { name: 'trocar de aba' }));

    expect(await screen.findByText('navegação bloqueada')).toBeInTheDocument();
  });

  it('confirmar prossegue com a navegação bloqueada', async () => {
    const user = userEvent.setup();
    renderHarness(true);

    await user.click(screen.getByRole('link', { name: 'ir para outra rota' }));
    await screen.findByText('navegação bloqueada');
    await user.click(screen.getByRole('button', { name: 'sair mesmo assim' }));

    expect(await screen.findByText('outra rota')).toBeInTheDocument();
  });

  it('cancelar desfaz o bloqueio e permanece na tela atual', async () => {
    const user = userEvent.setup();
    renderHarness(true);

    await user.click(screen.getByRole('link', { name: 'ir para outra rota' }));
    await screen.findByText('navegação bloqueada');
    await user.click(screen.getByRole('button', { name: 'continuar editando' }));

    expect(await screen.findByText('navegação livre')).toBeInTheDocument();
    expect(screen.queryByText('outra rota')).not.toBeInTheDocument();
  });
});
