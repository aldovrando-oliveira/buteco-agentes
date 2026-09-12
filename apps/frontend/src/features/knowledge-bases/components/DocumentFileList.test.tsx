import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { DocumentFileList, type DocumentFileEntry } from './DocumentFileList';
import { collectFiles } from '../utils/documentUpload';

function file(name: string, content = '# Conteúdo'): File {
  return new File([content], name, { type: 'text/plain' });
}

async function entriesFor(...files: File[]): Promise<DocumentFileEntry[]> {
  const collected = await collectFiles(files);
  return collected.map((f) => ({ file: f, send: { kind: 'idle' as const } }));
}

function renderList(overrides: Partial<Parameters<typeof DocumentFileList>[0]> = {}) {
  const handlers = {
    onEntriesAdded: vi.fn(),
    onTitleChange: vi.fn(),
    onRemove: vi.fn(),
  };

  const view = render(
    <MantineProvider theme={theme}>
      <DocumentFileList entries={[]} multiple {...handlers} {...overrides} />
    </MantineProvider>,
  );

  return { ...handlers, ...view };
}

describe('seleção pelo seletor de arquivos', () => {
  it('entrega os arquivos aceitos com título sugerido', async () => {
    const user = userEvent.setup();
    const { onEntriesAdded } = renderList();

    await user.upload(
      screen.getByTestId('document-dropzone').querySelector('input[type=file]') as HTMLElement,
      file('politica-de_reembolso.md'),
    );

    await waitFor(() => expect(onEntriesAdded).toHaveBeenCalled());
    expect(onEntriesAdded.mock.calls[0][0]).toEqual([
      expect.objectContaining({
        fileName: 'politica-de_reembolso.md',
        accepted: true,
        title: 'politica de reembolso',
      }),
    ]);
  });

  it('entrega vários arquivos de uma vez, na ordem da seleção', async () => {
    const user = userEvent.setup();
    const { onEntriesAdded } = renderList();

    await user.upload(
      screen.getByTestId('document-dropzone').querySelector('input[type=file]') as HTMLElement,
      [file('primeiro.md'), file('segundo.markdown'), file('terceiro.txt')],
    );

    await waitFor(() => expect(onEntriesAdded).toHaveBeenCalled());
    expect(onEntriesAdded.mock.calls[0][0].map((f: { fileName: string }) => f.fileName)).toEqual([
      'primeiro.md',
      'segundo.markdown',
      'terceiro.txt',
    ]);
  });

  // Medido, e é o que sustenta a existência de `isAcceptedExtension`: o
  // `userEvent.upload` respeita o atributo `accept` do input e DESCARTA o `.pdf`
  // antes de ele chegar ao componente — emulando o seletor do navegador, que não
  // oferece o arquivo.
  //
  // Ou seja, o `accept` já barra boa parte do caminho do SELETOR, e não barra
  // NADA do caminho de arrastar (ver o bloco de convergência abaixo, em que o
  // `.pdf` chega e é recusado com motivo). A validação no cliente não é
  // redundante com o `accept`: ela é a única que existe para o arrastar.
  it('o atributo accept do seletor já descarta extensão não aceita', async () => {
    const user = userEvent.setup();
    const { onEntriesAdded } = renderList();

    await user.upload(
      screen.getByTestId('document-dropzone').querySelector('input[type=file]') as HTMLElement,
      [file('primeiro.md'), file('segundo.pdf')],
    );

    await waitFor(() => expect(onEntriesAdded).toHaveBeenCalled());
    expect(onEntriesAdded.mock.calls[0][0].map((f: { fileName: string }) => f.fileName)).toEqual([
      'primeiro.md',
    ]);
  });
});

// TESTE DE CONVERGÊNCIA (design.md, D13).
//
// O que ele PROVA: as duas entradas — seletor e arrastar — chegam à mesma função
// de tratamento com os mesmos dados, produzindo resultado idêntico. Com isso, o
// teste do caminho do seletor (acima, com File real através de um <input> real)
// cobre a lógica dos DOIS caminhos, e só o GESTO de arrastar fica sem cobertura.
//
// O que ele NÃO PROVA, e isso fica dito em vez de escondido (convenção 11): o
// `dataTransfer` abaixo é um objeto FORJADO no teste. jsdom 29 não implementa
// DataTransfer nem DragEvent (medido) — o @testing-library/dom trata
// `dataTransfer` como caso especial do eventInit (dist/events.js:72-77) e o
// anexa ao evento. Logo, isto não prova que um navegador real entrega essa forma
// nesse evento.
//
// E há um defeito que NEM ISTO pega: `onDragOver` sem preventDefault faz o
// navegador nunca disparar `drop` e navegar para o arquivo, com a suíte verde e
// a área de soltar morta. É item nomeado da conferência manual.
describe('convergência das duas entradas de arquivo', () => {
  it('arrastar e escolher pelo seletor produzem o mesmo resultado', async () => {
    const user = userEvent.setup();

    const porSeletor = renderList();
    await user.upload(
      screen.getByTestId('document-dropzone').querySelector('input[type=file]') as HTMLElement,
      file('politica-de_reembolso.md', '# Reembolso'),
    );
    await waitFor(() => expect(porSeletor.onEntriesAdded).toHaveBeenCalled());
    const resultadoSeletor = porSeletor.onEntriesAdded.mock.calls[0][0];
    porSeletor.unmount();

    const porArrastar = renderList();
    fireEvent.drop(screen.getByTestId('document-dropzone'), {
      dataTransfer: { files: [file('politica-de_reembolso.md', '# Reembolso')] },
    });
    await waitFor(() => expect(porArrastar.onEntriesAdded).toHaveBeenCalled());
    const resultadoArrastar = porArrastar.onEntriesAdded.mock.calls[0][0];

    expect(resultadoArrastar).toEqual(resultadoSeletor);
  });

  it('arrastar também recusa extensão não aceita, com o mesmo motivo', async () => {
    const { onEntriesAdded } = renderList();

    fireEvent.drop(screen.getByTestId('document-dropzone'), {
      dataTransfer: { files: [file('manual.pdf')] },
    });

    await waitFor(() => expect(onEntriesAdded).toHaveBeenCalled());
    expect(onEntriesAdded.mock.calls[0][0][0]).toMatchObject({
      accepted: false,
      error: expect.stringContaining('.pdf'),
    });
  });
});

describe('linhas de arquivo', () => {
  it('arquivo recusado entra na lista com o motivo, não é descartado em silêncio', async () => {
    renderList({ entries: await entriesFor(file('manual.pdf')) });

    expect(screen.getByTestId('file-rejected-0')).toHaveTextContent(/\.pdf não é aceito/);
    expect(screen.getByText('manual.pdf')).toBeInTheDocument();
  });

  it('título é editável e avisa o índice da linha', async () => {
    const user = userEvent.setup();
    const { onTitleChange } = renderList({ entries: await entriesFor(file('politica.md')) });

    await user.type(screen.getByLabelText('Título do documento'), '!');

    expect(onTitleChange).toHaveBeenCalledWith(0, expect.stringContaining('!'));
  });

  it('cada linha pode ser removida individualmente', async () => {
    const user = userEvent.setup();
    const { onRemove } = renderList({
      entries: await entriesFor(file('primeiro.md'), file('segundo.md')),
    });

    await user.click(screen.getByRole('button', { name: 'Remover segundo.md' }));

    expect(onRemove).toHaveBeenCalledWith(1);
  });

  it('linha já criada não oferece remoção nem edição de título', async () => {
    const entries = await entriesFor(file('primeiro.md'));
    renderList({ entries: [{ ...entries[0], send: { kind: 'created' } }] });

    expect(screen.getByTestId('file-created-0')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remover primeiro.md' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Título do documento')).not.toBeInTheDocument();
  });

  it('linha que falhou exibe o motivo do envio', async () => {
    const entries = await entriesFor(file('primeiro.md'));
    renderList({
      entries: [{ ...entries[0], send: { kind: 'failed', error: 'Erro 400 ao criar.' } }],
    });

    expect(screen.getByTestId('file-failed-0')).toHaveTextContent('Erro 400 ao criar.');
  });
});
