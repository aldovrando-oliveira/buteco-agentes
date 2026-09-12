import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { KnowledgeDocumentsCard } from './KnowledgeDocumentsCard';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';

function doc(overrides: Partial<KnowledgeDocumentSummary> = {}): KnowledgeDocumentSummary {
  return {
    id: 'd1',
    knowledgeBaseId: 'k1',
    title: 'Faixas de atraso e descontos',
    sourceType: 'markdown',
    contentLengthBytes: 8420,
    indexingStatus: 'Indexed',
    indexedAt: '2026-09-02T03:14:00Z',
    failureReason: null,
    contentRevision: 1,
    fragmentCount: 14,
    indexingAttempts: 1,
    lastAttemptAt: '2026-09-02T03:14:00Z',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-02T00:00:00Z',
    ...overrides,
  };
}

function renderCard(props: Partial<Parameters<typeof KnowledgeDocumentsCard>[0]> = {}) {
  const handlers = {
    onAdd: vi.fn(),
    onUpdate: vi.fn(),
    onDelete: vi.fn(),
    onReindex: vi.fn(),
  };

  render(
    <MantineProvider theme={theme}>
      <KnowledgeDocumentsCard
        documents={[doc()]}
        isLoading={false}
        error={null}
        {...handlers}
        {...props}
      />
    </MantineProvider>,
  );

  return handlers;
}

describe('listagem', () => {
  it('exibe uma linha por documento, com título e tipo de origem', () => {
    renderCard({
      documents: [
        doc({ id: 'a', title: 'Faixas de atraso' }),
        doc({ id: 'b', title: 'Parcelamento' }),
        doc({ id: 'c', title: 'Aprovação humana' }),
      ],
    });

    expect(screen.getByText('Faixas de atraso')).toBeInTheDocument();
    expect(screen.getByText('Parcelamento')).toBeInTheDocument();
    expect(screen.getByText('Aprovação humana')).toBeInTheDocument();
    expect(screen.getAllByText('markdown')).toHaveLength(3);
  });

  it('preserva a ordem da resposta', () => {
    renderCard({
      documents: [
        doc({ id: 'a', title: 'Terceiro por nome' }),
        doc({ id: 'b', title: 'Primeiro por nome' }),
      ],
    });

    const linhas = screen.getAllByTestId(/^document-row-/);
    expect(linhas.map((l) => l.getAttribute('data-testid'))).toEqual([
      'document-row-a',
      'document-row-b',
    ]);
  });

  it('base sem documentos exibe estado vazio verificado', () => {
    renderCard({ documents: [] });

    expect(screen.getByTestId('documents-empty')).toHaveTextContent('Nenhum documento nesta base.');
  });

  // Uma requisição que não respondeu não é evidência de ausência.
  it('falha ao listar não vira estado vazio', () => {
    renderCard({ documents: undefined, error: new Error('falhou') });

    expect(screen.getByTestId('documents-load-error')).toBeInTheDocument();
    expect(screen.queryByTestId('documents-empty')).not.toBeInTheDocument();
    expect(screen.queryByText(/Nenhum documento nesta base/)).not.toBeInTheDocument();
  });
});

describe('contagem de fragmentos', () => {
  it('documento indexado exibe a contagem', () => {
    renderCard({ documents: [doc({ indexingStatus: 'Indexed', fragmentCount: 14 })] });

    expect(screen.getByText('14 fragmentos')).toBeInTheDocument();
  });

  it('documento novo em Pending não exibe contagem', () => {
    renderCard({
      documents: [doc({ indexingStatus: 'Pending', indexedAt: null, fragmentCount: 0 })],
    });

    expect(screen.queryByText(/fragmento/)).not.toBeInTheDocument();
  });

  it('documento que falhou depois de indexado exibe a contagem anterior', () => {
    renderCard({
      documents: [
        doc({
          indexingStatus: 'Failed',
          indexedAt: '2026-09-02T03:14:00Z',
          fragmentCount: 9,
          failureReason: '429 do provedor.',
        }),
      ],
    });

    expect(screen.getByText('9 fragmentos')).toBeInTheDocument();
  });

  it('documento reindexando exibe a contagem anterior', () => {
    renderCard({
      documents: [
        doc({ indexingStatus: 'Indexing', indexedAt: '2026-09-02T03:14:00Z', fragmentCount: 11 }),
      ],
    });

    expect(screen.getByText('11 fragmentos')).toBeInTheDocument();
  });

  // ASSERÇÃO NEGATIVA. É o defeito do protótipo, que escreve '0 fragmentos' no
  // estado que falhou — e é a regressão bem-intencionada de "preencher a célula
  // vazia" que esta asserção existe para impedir.
  it('documento que falhou sem nunca ter sido indexado não exibe "0 fragmentos"', () => {
    renderCard({
      documents: [
        doc({
          indexingStatus: 'Failed',
          indexedAt: null,
          fragmentCount: 0,
          failureReason: '429 do provedor.',
        }),
      ],
    });

    expect(screen.queryByText('0 fragmentos')).not.toBeInTheDocument();
    expect(screen.queryByText(/fragmento/)).not.toBeInTheDocument();
  });
});

describe('estado de indexação', () => {
  it('os quatro estados têm rótulo próprio e distinto', () => {
    renderCard({
      documents: [
        doc({ id: 'a', indexingStatus: 'Pending', indexedAt: null }),
        doc({ id: 'b', indexingStatus: 'Indexing', indexedAt: null }),
        doc({ id: 'c', indexingStatus: 'Indexed' }),
        doc({ id: 'd', indexingStatus: 'Failed', indexedAt: null, failureReason: 'erro' }),
      ],
    });

    expect(screen.getByText('Pendente')).toBeInTheDocument();
    expect(screen.getByText('Indexando')).toBeInTheDocument();
    expect(screen.getByText('Indexado')).toBeInTheDocument();
    expect(screen.getByText('Falhou')).toBeInTheDocument();
  });

  it('Indexing exibe indicador de atividade', () => {
    renderCard({ documents: [doc({ id: 'x', indexingStatus: 'Indexing', indexedAt: null })] });

    expect(screen.getByTestId('document-indexing-x')).toBeInTheDocument();
  });

  it('Pending e Indexing dizem o que está acontecendo', () => {
    renderCard({
      documents: [
        doc({ id: 'a', indexingStatus: 'Pending', indexedAt: null }),
        doc({ id: 'b', indexingStatus: 'Indexing', indexedAt: null }),
      ],
    });

    expect(screen.getByText('na fila de indexação')).toBeInTheDocument();
    expect(screen.getByText('gerando embeddings')).toBeInTheDocument();
  });

  // ASSERÇÃO NEGATIVA. O sistema conhece o ESTADO do documento, não o
  // percentual: IndexingAttempts conta execuções, não fração de trabalho, e não
  // existe denominador em campo nenhum (convenção 13).
  it('nenhum estado exibe progresso percentual', () => {
    const { container } = render(
      <MantineProvider theme={theme}>
        <KnowledgeDocumentsCard
          documents={[
            doc({ id: 'a', indexingStatus: 'Pending', indexedAt: null }),
            doc({ id: 'b', indexingStatus: 'Indexing', indexedAt: null }),
          ]}
          isLoading={false}
          error={null}
          onAdd={vi.fn()}
          onUpdate={vi.fn()}
          onDelete={vi.fn()}
          onReindex={vi.fn()}
        />
      </MantineProvider>,
    );

    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(container.textContent).not.toMatch(/\d+\s*%/);
    expect(container.textContent).not.toMatch(/\d+\s*de\s*\d+/);
  });
});

describe('faixa de falha', () => {
  const motivoLongo =
    'O provedor de embedding devolveu 429 (rate limit) nas três tentativas de indexação deste ' +
    'documento, a última em 01/09/2026 às 03:14. O texto continua armazenado na base, mas nenhum ' +
    'fragmento dele está no índice e ele não aparece nas consultas do agente.';

  it('exibe o motivo completo, sem truncar', () => {
    renderCard({
      documents: [
        doc({ id: 'f', indexingStatus: 'Failed', indexedAt: null, failureReason: motivoLongo }),
      ],
    });

    expect(screen.getByTestId('failure-reason-f')).toHaveTextContent(motivoLongo);
  });

  it('a faixa oferece reindexar, e o callback recebe o documento', async () => {
    const user = userEvent.setup();
    const handlers = renderCard({
      documents: [
        doc({ id: 'f', indexingStatus: 'Failed', indexedAt: null, failureReason: 'erro' }),
      ],
    });

    await user.click(screen.getByRole('button', { name: 'Reindexar documento' }));

    expect(handlers.onReindex).toHaveBeenCalledWith(expect.objectContaining({ id: 'f' }));
  });

  it('documentos que não falharam não têm faixa', () => {
    renderCard({
      documents: [
        doc({ id: 'a', indexingStatus: 'Pending', indexedAt: null }),
        doc({ id: 'b', indexingStatus: 'Indexing', indexedAt: null }),
        doc({ id: 'c', indexingStatus: 'Indexed' }),
      ],
    });

    expect(screen.queryByRole('button', { name: 'Reindexar documento' })).not.toBeInTheDocument();
    expect(screen.queryByText('Falhou ao indexar')).not.toBeInTheDocument();
  });

  it('falha sem motivo registrado não inventa um motivo', () => {
    renderCard({
      documents: [doc({ id: 'f', indexingStatus: 'Failed', indexedAt: null, failureReason: null })],
    });

    expect(screen.getByTestId('failure-reason-f')).toHaveTextContent(
      /motivo desta falha não foi registrado/i,
    );
  });
});

describe('faixa de resumo', () => {
  it('aparece com documento não-terminal, dizendo quantos', () => {
    renderCard({
      documents: [
        doc({ id: 'a', indexingStatus: 'Indexed' }),
        doc({ id: 'b', indexingStatus: 'Pending', indexedAt: null }),
        doc({ id: 'c', indexingStatus: 'Indexing', indexedAt: null }),
      ],
    });

    expect(screen.getByTestId('documents-pending-summary')).toHaveTextContent(
      '2 documentos ainda não estão utilizáveis',
    );
  });

  it('não aparece com todos terminais', () => {
    renderCard({
      documents: [
        doc({ id: 'a', indexingStatus: 'Indexed' }),
        doc({ id: 'b', indexingStatus: 'Failed', indexedAt: null, failureReason: 'erro' }),
      ],
    });

    expect(screen.queryByTestId('documents-pending-summary')).not.toBeInTheDocument();
  });
});

describe('ações e cópia', () => {
  it('adicionar, atualizar e excluir chamam os callbacks', async () => {
    const user = userEvent.setup();
    const handlers = renderCard({ documents: [doc({ id: 'z' })] });

    await user.click(screen.getByRole('button', { name: 'Adicionar documento' }));
    await user.click(screen.getByRole('button', { name: 'Atualizar' }));
    await user.click(screen.getByRole('button', { name: 'Excluir' }));

    expect(handlers.onAdd).toHaveBeenCalled();
    expect(handlers.onUpdate).toHaveBeenCalledWith(expect.objectContaining({ id: 'z' }));
    expect(handlers.onDelete).toHaveBeenCalledWith(expect.objectContaining({ id: 'z' }));
  });

  // ASSERÇÃO NEGATIVA (convenção 13). Nada correlaciona tamanho com falha de
  // embedding em apps/workers, e o conselho do protótipo não vira nem texto
  // estático de ajuda.
  it('nenhuma cópia relaciona tamanho de documento a falha de indexação', () => {
    const { container } = render(
      <MantineProvider theme={theme}>
        <KnowledgeDocumentsCard
          documents={[
            doc({ id: 'f', indexingStatus: 'Failed', indexedAt: null, failureReason: 'erro' }),
          ]}
          isLoading={false}
          error={null}
          onAdd={vi.fn()}
          onUpdate={vi.fn()}
          onDelete={vi.fn()}
          onReindex={vi.fn()}
        />
      </MantineProvider>,
    );

    expect(container.textContent).not.toMatch(/grandes?\b/i);
    expect(container.textContent).not.toMatch(/limite de requisi/i);
  });
});
