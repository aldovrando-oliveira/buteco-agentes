import { describe, expect, it } from 'vitest';
import { MantineProvider } from '@mantine/core';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { AccessDenied } from './AccessDenied';
import { ActionDenied } from './ActionDenied';

const wrap = (ui: React.ReactNode) =>
  render(
    <MantineProvider>
      <MemoryRouter>{ui}</MemoryRouter>
    </MantineProvider>,
  );

// Spec `panel-roles`, "Tela de acesso negado" e "O 403 na carga e na ação".
describe('acesso negado', () => {
  it('Os textos e o link', () => {
    wrap(<AccessDenied />);

    const alerta = screen.getByRole('alert');
    expect(alerta).toHaveTextContent('Você não tem acesso a esta página.');
    expect(alerta).toHaveTextContent(
      'Se o seu papel mudou há pouco, a mudança pode levar até 15 minutos para chegar a todo o painel.',
    );
    expect(screen.getByRole('link', { name: 'Ir para o Inventário' })).toHaveAttribute(
      'href',
      '/inventory',
    );
    expect(alerta.textContent).not.toMatch(/Admin|Operador|Leitor/);
  });

  it('o aviso da ação recusada', () => {
    wrap(<ActionDenied />);

    const alerta = screen.getByRole('alert');
    expect(alerta).toHaveTextContent('Você não tem permissão para esta ação.');
    expect(alerta).toHaveTextContent('Se o seu papel mudou há pouco');
    expect(alerta.textContent).not.toMatch(/Admin|Operador|Leitor/);
  });
});
