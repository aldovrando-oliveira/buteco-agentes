import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider, Radio } from '@mantine/core';
import { RadioCardOption } from './RadioCardOption';
import { theme } from '../../theme';

function renderGroup(value: string, onChange = vi.fn()) {
  render(
    <MantineProvider theme={theme}>
      <Radio.Group value={value} onChange={onChange} aria-label="Escolha">
        <RadioCardOption value="a" title="Opção A" description="Descrição da A" />
        <RadioCardOption
          value="b"
          title="Opção B"
          description="Descrição da B"
          disabled
          note="Indisponível nesta instalação."
        />
      </Radio.Group>
    </MantineProvider>,
  );
  return onChange;
}

describe('RadioCardOption', () => {
  it('é um radio com o título no nome acessível e a descrição visível', () => {
    renderGroup('a');

    expect(screen.getByRole('radio', { name: /Opção A/ })).toBeInTheDocument();
    expect(screen.getByText('Descrição da A')).toBeInTheDocument();
  });

  it('marca a opção do valor do grupo e só ela', () => {
    renderGroup('a');

    expect(screen.getByRole('radio', { name: /Opção A/ })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: /Opção B/ })).toHaveAttribute('aria-checked', 'false');
  });

  it('clicar na opção avisa o grupo com o valor dela', async () => {
    const onChange = renderGroup('b');

    await userEvent.setup().click(screen.getByRole('radio', { name: /Opção A/ }));

    expect(onChange).toHaveBeenCalledWith('a');
  });

  it('mostra a nota só quando ela é passada', () => {
    renderGroup('a');

    expect(screen.getByText('Indisponível nesta instalação.')).toBeInTheDocument();
    expect(screen.getAllByText(/Indisponível/)).toHaveLength(1);
  });

  it('opção desabilitada fica desabilitada', () => {
    renderGroup('a');

    expect(screen.getByRole('radio', { name: /Opção B/ })).toBeDisabled();
  });

  it('usa a classe que estiliza o estado marcado', () => {
    renderGroup('a');

    expect(screen.getByRole('radio', { name: /Opção A/ })).toHaveClass('buteco-radio-card-option');
  });
});
