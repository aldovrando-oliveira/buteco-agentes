import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { ConversationModelsCard } from './ConversationModelsCard';
import type { ModelTokens } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';

function renderCard(byModel: ModelTokens[], queryState: QueryState = 'ok') {
  render(
    <MantineProvider theme={theme}>
      <ConversationModelsCard
        byModel={byModel}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
}

const modelos: ModelTokens[] = [
  { provider: 'openai', model: 'gpt-5.6-sol', totalTokens: 3_200_000, callCount: 251 },
  { provider: 'anthropic', model: 'claude-opus-5', totalTokens: 5_000_000, callCount: 312 },
  { provider: 'google', model: 'gemini-2.5-pro', totalTokens: 900_000, callCount: 41 },
];

describe('ConversationModelsCard', () => {
  it('ordena por tokens, do maior para o menor', () => {
    renderCard(modelos);

    // Pelas LINHAS, e não por um regex de testid: `modelo-<nome>-tokens` e
    // `modelo-<nome>-chamadas` também casariam, e a ordem afirmada não seria a
    // das linhas.
    const linhas = screen.getAllByTestId(/^modelo-/).filter((el) =>
      el.hasAttribute('data-model-row'),
    );
    expect(linhas.map((l) => l.getAttribute('data-testid'))).toEqual([
      'modelo-claude-opus-5',
      'modelo-gpt-5.6-sol',
      'modelo-gemini-2.5-pro',
    ]);
  });

  it('o modelo com tokens NULOS vai para o fim, e não disputa como se fosse o menor', () => {
    // Consumo desconhecido não é consumo baixo. Ordenar o nulo como 0 o
    // colocaria junto dos modelos que realmente consumiram pouco, afirmando
    // uma comparação que ninguém pode fazer.
    renderCard([
      { provider: 'x', model: 'sem-relato', totalTokens: null, callCount: 9 },
      { provider: 'y', model: 'pouco', totalTokens: 100, callCount: 2 },
    ]);

    const linhas = screen.getAllByTestId(/^modelo-/).filter((el) =>
      el.hasAttribute('data-model-row'),
    );
    expect(linhas[0]).toHaveAttribute('data-testid', 'modelo-pouco');
    expect(linhas[1]).toHaveAttribute('data-testid', 'modelo-sem-relato');
  });

  it('O PAR QUE SEPARA OS DOIS ESTADOS: tokens nulo vazio, callCount zero escrito', () => {
    // Os dois na MESMA linha, que é o ponto: `totalTokens` é `long?` e
    // `callCount` é `int`. Um `?? 0` que passasse pelos dois faria a linha
    // dizer "0 tokens em 0 chamadas", que é plausível e errado na metade.
    renderCard([
      { provider: 'x', model: 'sem-relato', totalTokens: null, callCount: 0 },
    ]);

    const tokens = screen.getByTestId('modelo-sem-relato-tokens');
    const chamadas = screen.getByTestId('modelo-sem-relato-chamadas');

    expect(tokens).toHaveAttribute('data-metric-state', 'empty');
    expect(tokens.textContent).not.toContain('0');

    expect(chamadas).toHaveAttribute('data-metric-state', 'zero');
    expect(chamadas).toHaveTextContent('0');
  });

  it('NEGATIVO: a coluna Cache sai, e NADA entra no lugar dela', () => {
    // O protótipo desenha a coluna e a rota não a serve. Ela sai — e **nenhum
    // elemento novo** é acrescentado ao card para anunciá-la.
    //
    // Houve ali um quadro tracejado dizendo "Cache lido por modelo — não
    // disponível". Ele não existe no `Main.dc.html`, e o peso dele competia com
    // os números do card. Removido por decisão do dono (décima rodada): o que
    // sobrevive ao archive é a issue #66, não um quadro na tela.
    renderCard(modelos);

    expect(screen.queryByRole('columnheader', { name: /cache/i })).toBeNull();
    expect(screen.queryByTestId('modelos-lacuna-cache')).toBeNull();
    expect(screen.getByTestId('card-modelos-de-conversa').querySelector('[data-declared-gap]')).toBeNull();
    expect(screen.getByTestId('card-modelos-de-conversa').textContent).not.toMatch(/cache/i);
  });

  it('as quatro colunas do protótipo estão lá', () => {
    renderCard(modelos);

    for (const coluna of ['Modelo', 'Provedor', 'Chamadas', 'Tokens']) {
      expect(screen.getByRole('columnheader', { name: coluna })).toBeInTheDocument();
    }
  });

  it('a nota sobre a ordem está presente', () => {
    renderCard(modelos);

    expect(screen.getByTestId('nota-ordenacao')).toHaveTextContent(/mais chamado não é/i);
  });

  it('sem nenhum modelo, diz o zero medido por extenso', () => {
    renderCard([]);

    expect(screen.getByTestId('modelos-vazio')).toHaveTextContent(
      'Nenhuma chamada a modelo neste período.',
    );
  });

  it('consulta sem resposta põe travessão nas duas colunas numéricas', () => {
    renderCard(modelos, 'failed');

    expect(screen.getByTestId('modelo-claude-opus-5-tokens')).toHaveAttribute(
      'data-metric-state',
      'unknown',
    );
    expect(screen.getByTestId('modelo-claude-opus-5-chamadas')).toHaveAttribute(
      'data-metric-state',
      'unknown',
    );
  });
});
