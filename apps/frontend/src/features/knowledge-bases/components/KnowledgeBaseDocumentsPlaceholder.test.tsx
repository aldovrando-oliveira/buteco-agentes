import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { KnowledgeBaseDocumentsPlaceholder } from './KnowledgeBaseDocumentsPlaceholder';

function renderPlaceholder() {
  return render(
    <MantineProvider theme={theme}>
      <KnowledgeBaseDocumentsPlaceholder />
    </MantineProvider>,
  );
}

describe('KnowledgeBaseDocumentsPlaceholder', () => {
  it('informa que a gestão de documentos chega em etapa posterior', () => {
    renderPlaceholder();

    expect(screen.getByTestId('documents-placeholder')).toHaveTextContent(/próxima etapa/i);
  });

  // Asserção negativa (convenção 13): é ela que impede a regressão de trocar a
  // nota de sequenciamento por um estado vazio. "Nenhum documento" afirmaria que
  // a base foi consultada e está vazia, quando esta etapa não consulta documento
  // nenhum (design.md, D4).
  it('não afirma que a base está sem documentos', () => {
    renderPlaceholder();

    expect(screen.queryByText(/nenhum documento/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/vazia/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/0 documento/i)).not.toBeInTheDocument();
  });

  it('diz por que não há contagem, em vez de omitir sem explicação', () => {
    renderPlaceholder();

    expect(screen.getByTestId('documents-placeholder')).toHaveTextContent(
      /não consulta os documentos/i,
    );
  });
});
