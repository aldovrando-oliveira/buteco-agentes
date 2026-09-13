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

  // P4 — "entra na", não "é a". Desde a etapa 4 a descrição da tool é uma cadeia
  // montada (KnowledgeToolDescription.cs:52), e o texto cadastrado é a parte que o
  // operador escreve.
  it('diz que o texto entra na descrição da ferramenta, envolvido por instruções fixas', () => {
    renderCard();

    expect(screen.getByText(/entra na descrição da ferramenta/i)).toBeInTheDocument();
    expect(screen.getByText(/envolvido por instruções fixas do sistema/i)).toBeInTheDocument();
  });

  // N8 — a asserção negativa MORA AQUI, no componente que carrega a frase, e não
  // no teste do formulário (design.md, D6; convenção 15, segunda forma). O
  // formulário carrega a MESMA correção em outra forma — o preview se intitulando
  // como o texto completo —, e o guarda dele é N6, em KnowledgeBaseForm.test.tsx.
  // Reintroduzir a frase aqui tem de reprovar só este teste; reintroduzi-la lá,
  // só aquele.
  it('não afirma que a descrição cadastrada É a descrição da ferramenta', () => {
    renderCard();

    expect(screen.queryByText(/é a descrição da ferramenta/i)).not.toBeInTheDocument();
  });

  it('preserva as quebras de linha do texto cadastrado', () => {
    renderCard('Primeira linha.\nSegunda linha.');

    expect(screen.getByTestId('knowledge-base-description')).toHaveStyle({
      whiteSpace: 'pre-wrap',
    });
  });
});
