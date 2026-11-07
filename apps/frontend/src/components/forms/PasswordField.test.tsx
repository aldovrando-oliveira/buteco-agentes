import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../theme';
import { PasswordField } from './PasswordField';

function renderField(error?: string) {
  render(
    <MantineProvider theme={theme}>
      <PasswordField label="Token" error={error} />
    </MantineProvider>,
  );
  return screen.getByLabelText('Token');
}

describe('PasswordField', () => {
  it('com erro, o input é anunciado como inválido e aponta para a mensagem', () => {
    const input = renderField('O token de autenticação é obrigatório.');

    expect(input).toHaveAttribute('aria-invalid', 'true');
    const ids = (input.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(Boolean);
    expect(ids.map((id) => document.getElementById(id)?.textContent)).toContain(
      'O token de autenticação é obrigatório.',
    );
  });

  it('sem erro, o input não tem aria-invalid', () => {
    expect(renderField()).not.toHaveAttribute('aria-invalid');
  });
});
