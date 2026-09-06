import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { SectionLabel } from './SectionLabel';
import { theme } from '../../theme';

describe('SectionLabel', () => {
  it('aplica caixa alta e espaçamento entre letras', () => {
    render(
      <MantineProvider theme={theme} defaultColorScheme="light">
        <SectionLabel>Catálogo de tools</SectionLabel>
      </MantineProvider>,
    );

    const rotulo = screen.getByText('Catálogo de tools');

    expect(rotulo).toHaveStyle({ letterSpacing: '0.05em' });
    expect(rotulo).toHaveStyle({ textTransform: 'uppercase' });
  });
});
