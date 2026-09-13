import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { KnowledgeBaseDescriptionCard } from './KnowledgeBaseDescriptionCard';

const description = 'Regras de negociação, prazos e faixas de desconto.';

function renderCard(value = description) {
  return render(
    <MantineProvider theme={theme}>
      <KnowledgeBaseDescriptionCard description={value} />
    </MantineProvider>,
  );
}

describe('KnowledgeBaseDescriptionCard', () => {
  it('exibe a descrição em seção própria', () => {
    renderCard();

    expect(screen.getByTestId('knowledge-base-description')).toHaveTextContent(description);
  });

  // O rótulo é o que impede a descrição de ser lida como texto de UI: ela é o
  // texto que o modelo lê para decidir se a base é relevante (design.md, D7).
  it('rotula a seção como o texto lido pelo modelo', () => {
    renderCard();

    expect(screen.getByText(/texto lido pelo modelo/i)).toBeInTheDocument();
  });

  it('diz que o texto não é mostrado ao cliente e para que serve', () => {
    renderCard();

    expect(screen.getByText(/não é mostrado ao cliente/i)).toBeInTheDocument();
    expect(
      screen.getByText(/o modelo decide se a pergunta pertence a esta base/i),
    ).toBeInTheDocument();
  });

  it('preserva as quebras de linha do texto cadastrado', () => {
    renderCard('Primeira linha.\nSegunda linha.');

    expect(screen.getByTestId('knowledge-base-description')).toHaveStyle({
      whiteSpace: 'pre-wrap',
    });
  });
});
