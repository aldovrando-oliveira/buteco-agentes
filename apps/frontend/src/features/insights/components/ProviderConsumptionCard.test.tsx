import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { ProviderConsumptionCard } from './ProviderConsumptionCard';
import type { ProviderTokens } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';

function renderCard(byProvider: ProviderTokens[], queryState: QueryState = 'ok') {
  render(
    <MantineProvider theme={theme}>
      <ProviderConsumptionCard
        byProvider={byProvider}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
}

/** Como o `Main.dc.html` desenha: `anthropic` sem embedding, `openai` com. */
const provedores: ProviderTokens[] = [
  {
    provider: 'anthropic',
    conversationInputTokens: 6_400_000,
    conversationOutputTokens: 1_900_000,
    embeddingInputTokens: null,
  },
  {
    provider: 'openai',
    conversationInputTokens: 2_500_000,
    conversationOutputTokens: 700_000,
    embeddingInputTokens: 341_200,
  },
];

describe('ProviderConsumptionCard', () => {
  it('NEGATIVO: provedor sem aquele tipo de chamada tem célula vazia, SEM 0', () => {
    // O guarda central desta tabela. Escrever 0 na coluna Embedding do
    // `anthropic` inventaria uma economia que ninguém mediu — e é o tipo de
    // célula que alguém "conserta" na próxima change.
    renderCard(provedores);

    const celula = screen.getByTestId('provedor-anthropic-embedding');
    expect(celula).toHaveAttribute('data-metric-state', 'empty');
    expect(celula.textContent).not.toContain('0');
  });

  it('o provedor que reporta mostra o número', () => {
    renderCard(provedores);

    expect(screen.getByTestId('provedor-openai-embedding')).toHaveTextContent('341,2 mil');
  });

  it('o total soma só o que é conhecido', () => {
    renderCard(provedores);

    // anthropic: 6,4 M + 1,9 M de conversa, e NADA de embedding. O total é
    // 8,3 M — não 8,3 M + 0, que daria o mesmo número por acaso e o erro certo
    // por construção.
    expect(screen.getByTestId('provedor-anthropic-total')).toHaveTextContent('8,3 M');
    expect(screen.getByTestId('provedor-openai-total')).toHaveTextContent('3,5 M');
  });

  it('NEGATIVO: total com TODAS as parcelas nulas fica vazio, e não 0', () => {
    renderCard([
      {
        provider: 'gemini',
        conversationInputTokens: null,
        conversationOutputTokens: null,
        embeddingInputTokens: null,
      },
    ]);

    const total = screen.getByTestId('provedor-gemini-total');
    expect(total).toHaveAttribute('data-metric-state', 'empty');
    expect(total.textContent).not.toContain('0');
  });

  it('uma parcela nula e outra não: o total é a conhecida', () => {
    renderCard([
      {
        provider: 'gemini',
        conversationInputTokens: 900_000,
        conversationOutputTokens: null,
        embeddingInputTokens: null,
      },
    ]);

    expect(screen.getByTestId('provedor-gemini-total')).toHaveTextContent('900 mil');
  });

  it('zero medido na conversa aparece como 0', () => {
    // A outra metade: se a soma das parcelas conhecidas dá zero, alguém contou.
    renderCard([
      {
        provider: 'gemini',
        conversationInputTokens: 0,
        conversationOutputTokens: 0,
        embeddingInputTokens: null,
      },
    ]);

    const conversa = screen.getByTestId('provedor-gemini-conversa');
    expect(conversa).toHaveAttribute('data-metric-state', 'zero');
    expect(conversa).toHaveTextContent('0');
  });

  it('o significado da célula vazia é DECLARADO em texto, literal do protótipo', () => {
    renderCard(provedores);

    expect(screen.getByTestId('nota-celula-vazia')).toHaveTextContent(
      'Célula vazia significa que o provedor não atende esse tipo de chamada neste sistema — não que o consumo seja zero.',
    );
  });

  it('sem nenhum provedor, diz o zero medido por extenso', () => {
    renderCard([]);

    expect(screen.getByTestId('consumo-por-provedor-vazio')).toHaveTextContent(
      'Nenhum consumo por provedor neste período.',
    );
  });

  it('consulta sem resposta põe travessão, e nenhum número', () => {
    renderCard(provedores, 'failed');

    expect(screen.getByTestId('provedor-anthropic-total')).toHaveAttribute(
      'data-metric-state',
      'unknown',
    );
    expect(screen.getByTestId('card-consumo-por-provedor').textContent).not.toContain('8,3 M');
  });
});
