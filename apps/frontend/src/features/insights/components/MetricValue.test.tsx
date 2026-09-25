import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { MetricValue } from './MetricValue';
import { formatTokens } from '../utils/metricState';

function renderValue(props: Parameters<typeof MetricValue>[0]) {
  return render(
    <MantineProvider theme={theme}>
      <MetricValue {...props} />
    </MantineProvider>,
  );
}

const alvo = () => screen.getByTestId('valor');

describe('MetricValue — os quatro estados renderizados', () => {
  it('valor medido aparece formatado', () => {
    renderValue({ value: 12_400_000, queryState: 'ok', format: formatTokens, 'data-testid': 'valor' });

    expect(alvo()).toHaveAttribute('data-metric-state', 'value');
    expect(alvo()).toHaveTextContent('12,4 M');
  });

  it('zero medido aparece como 0, escrito', () => {
    renderValue({ value: 0, queryState: 'ok', 'data-testid': 'valor' });

    expect(alvo()).toHaveAttribute('data-metric-state', 'zero');
    expect(alvo()).toHaveTextContent('0');
  });

  // ------------------------------------------- OS GUARDAS NEGATIVOS (D6)

  it('NEGATIVO: origem nula NÃO renderiza 0', () => {
    renderValue({ value: null, queryState: 'ok', 'data-testid': 'valor' });

    // A asserção é a AUSÊNCIA do zero, não a presença do vazio. Um teste que
    // só afirmasse "renderiza vazio" passaria por acidente se alguém trocasse
    // o vazio por outra coisa qualquer que não fosse zero (convenção 13).
    expect(alvo().textContent).not.toContain('0');
    expect(alvo()).toHaveAttribute('data-metric-state', 'empty');
  });

  it('NEGATIVO: origem indefinida NÃO renderiza 0', () => {
    renderValue({ value: undefined, queryState: 'ok', 'data-testid': 'valor' });

    expect(alvo().textContent).not.toContain('0');
  });

  it('NEGATIVO: o formatador de tokens não transforma nulo em "0"', () => {
    // O caminho mais provável do defeito: um formatador que recebe nulo,
    // coage para número e devolve "0". `readMetric` nunca o chama com nulo.
    renderValue({
      value: null,
      queryState: 'ok',
      format: formatTokens,
      'data-testid': 'valor',
    });

    expect(alvo().textContent).not.toContain('0');
    expect(alvo().textContent).not.toContain('M');
  });

  it('NEGATIVO: consulta sem resposta renderiza travessão e NENHUM 0', () => {
    renderValue({
      value: null,
      queryState: 'failed',
      reason: 'A consulta não respondeu.',
      'data-testid': 'valor',
    });

    expect(alvo()).toHaveAttribute('data-metric-state', 'unknown');
    expect(alvo()).toHaveTextContent('—');
    expect(alvo().textContent).not.toContain('0');
  });

  it('o travessão SEMPRE vem com a razão ao lado', () => {
    // Travessão sozinho é lido como zero — é o que o quadro 3 do
    // `Estados.dc.html` proíbe por escrito.
    renderValue({
      value: null,
      queryState: 'failed',
      reason: 'Tempo de resposta esgotado.',
      'data-testid': 'valor',
    });

    expect(alvo()).toHaveAttribute('title', 'Tempo de resposta esgotado.');
    expect(alvo()).toHaveAccessibleName(/Tempo de resposta esgotado/);
  });

  it('a unidade não aparece no vazio nem no travessão', () => {
    const { unmount } = renderValue({
      value: null,
      queryState: 'ok',
      unit: 's',
      'data-testid': 'valor',
    });
    expect(alvo().textContent).not.toContain('s');
    unmount();

    renderValue({ value: null, queryState: 'failed', unit: 's', 'data-testid': 'valor' });
    expect(alvo().textContent).not.toContain('s');
  });

  it('a unidade aparece no valor medido', () => {
    renderValue({ value: 12.4, queryState: 'ok', unit: 's', 'data-testid': 'valor' });

    expect(alvo()).toHaveTextContent('12,4 s');
  });

  it('a célula vazia preserva a linha sem exibir conteúdo', () => {
    renderValue({ value: null, queryState: 'ok', 'data-testid': 'valor' });

    // A linha existe (o elemento está no documento), e não há nada legível
    // dentro dela.
    expect(alvo()).toBeInTheDocument();
    expect(alvo().textContent?.trim()).toBe('');
  });
});
