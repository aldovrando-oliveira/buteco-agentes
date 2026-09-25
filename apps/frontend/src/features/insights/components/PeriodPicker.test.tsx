import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { PeriodPicker } from './PeriodPicker';
import { DEFAULT_INSIGHTS_PERIOD } from '../utils/insightsWindow';

function renderPicker(value = DEFAULT_INSIGHTS_PERIOD, onChange = vi.fn()) {
  render(
    <MantineProvider theme={theme}>
      <PeriodPicker value={value} onChange={onChange} />
    </MantineProvider>,
  );
  return onChange;
}

describe('PeriodPicker', () => {
  it('oferece os três períodos do protótipo', () => {
    renderPicker();

    expect(screen.getByRole('radio', { name: '7d' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '30d' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '90d' })).toBeInTheDocument();
  });

  it('aciona o callback com o período escolhido', async () => {
    const onChange = renderPicker();

    await userEvent.click(screen.getByRole('radio', { name: '7d' }));

    expect(onChange).toHaveBeenCalledWith('7d');
  });

  it('o selecionado é o ÚNICO marcado', () => {
    renderPicker('90d');

    expect(screen.getByRole('radio', { name: '90d' })).toBeChecked();
    expect(screen.getByRole('radio', { name: '7d' })).not.toBeChecked();
    expect(screen.getByRole('radio', { name: '30d' })).not.toBeChecked();
  });

  it('30d é o padrão da página', () => {
    renderPicker();

    expect(screen.getByRole('radio', { name: '30d' })).toBeChecked();
  });
});
