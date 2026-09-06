import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { BackLink } from './BackLink';
import { theme } from '../../theme';

describe('BackLink', () => {
  it('aponta para o destino por rota fixa, nomeando-o', () => {
    render(
      <MantineProvider theme={theme} defaultColorScheme="light">
        {/* Entrada única no histórico: não há para onde voltar, e o link
            precisa funcionar mesmo assim. */}
        <MemoryRouter initialEntries={['/agents/new']}>
          <BackLink to="/agents" label="Agentes" />
        </MemoryRouter>
      </MantineProvider>,
    );

    expect(screen.getByRole('link', { name: 'Agentes' })).toHaveAttribute('href', '/agents');
  });
});
