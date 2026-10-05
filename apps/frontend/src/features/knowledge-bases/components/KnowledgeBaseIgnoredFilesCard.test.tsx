import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { KnowledgeBaseIgnoredFilesCard } from './KnowledgeBaseIgnoredFilesCard';
import { formatSyncInstant } from '../utils/syncState';
import type { KnowledgeBaseIgnoredFile } from '../types/knowledgeBase';

const T_DONE = '2026-09-26T17:32:00Z';

function renderCard(props: {
  ignoredFiles: KnowledgeBaseIgnoredFile[] | null;
  lastCompletedAt?: string | null;
  failing?: boolean;
}) {
  render(
    <MantineProvider theme={theme}>
      <KnowledgeBaseIgnoredFilesCard
        ignoredFiles={props.ignoredFiles}
        lastCompletedAt={props.lastCompletedAt ?? null}
        failing={props.failing ?? false}
      />
    </MantineProvider>,
  );
  return screen.getByTestId('ignored-files-card');
}

const files: KnowledgeBaseIgnoredFile[] = [
  { externalRef: 'x1', name: 'Tabela de preços.xlsx', code: 'unsupported-type', detail: null },
  { externalRef: 'x2', name: 'Manual completo do produto', code: 'too-large', detail: '2097152' },
];

describe('arquivos da pasta que não entraram na base', () => {
  it('lista com nome e motivo, e a contagem no cabeçalho', () => {
    const card = renderCard({ ignoredFiles: files, lastCompletedAt: T_DONE });

    expect(card).toHaveTextContent('Arquivos da pasta que não entraram na base');
    expect(screen.getByText('2 arquivos')).toBeInTheDocument();
    expect(screen.getByText('Tabela de preços.xlsx')).toBeInTheDocument();
    expect(
      screen.getByText('Tipo não suportado. Só Google Docs e arquivos .md entram na base.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Manual completo do produto')).toBeInTheDocument();
    expect(
      screen.getByText('Maior que o limite de 1 MiB depois da exportação (2097152 bytes).'),
    ).toBeInTheDocument();
  });

  it('um arquivo: "1 arquivo"', () => {
    renderCard({ ignoredFiles: [files[0]], lastCompletedAt: T_DONE });
    expect(screen.getByText('1 arquivo')).toBeInTheDocument();
  });

  it('lista nula: aparece depois da primeira sincronização, e não "Nenhum arquivo"', () => {
    const card = renderCard({ ignoredFiles: null });

    expect(card).toHaveTextContent('A lista aparece depois da primeira sincronização concluída.');
    expect(card).not.toHaveTextContent('Nenhum arquivo');
    expect(card).not.toHaveTextContent(/\d+ arquivos?/);
  });

  it('lista vazia: nenhum recusado, e não "A lista aparece"', () => {
    const card = renderCard({ ignoredFiles: [], lastCompletedAt: T_DONE });

    expect(card).toHaveTextContent(
      'Nenhum arquivo foi recusado na última sincronização concluída.',
    );
    expect(card).not.toHaveTextContent('A lista aparece');
    expect(screen.getByText('Nenhum')).toBeInTheDocument();
  });

  it('em falha, a lista é a da última sincronização concluída, com a data', () => {
    renderCard({ ignoredFiles: files, lastCompletedAt: T_DONE, failing: true });

    expect(
      screen.getByText(`Lista da última sincronização concluída, em ${formatSyncInstant(T_DONE)}.`),
    ).toBeInTheDocument();
  });

  it('em dia, sem a linha de última concluída', () => {
    const card = renderCard({ ignoredFiles: files, lastCompletedAt: T_DONE });
    expect(card).not.toHaveTextContent('Lista da última sincronização concluída');
  });
});
