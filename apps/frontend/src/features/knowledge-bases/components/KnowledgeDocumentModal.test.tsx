import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { KnowledgeDocumentModal } from './KnowledgeDocumentModal';
import type { KnowledgeDocument } from '../types/knowledgeDocument';

const CONTEUDO_ATUAL = '# Campanhas 2025\nRelatório consolidado por mês.';

const documento: KnowledgeDocument = {
  id: 'd1',
  knowledgeBaseId: 'k1',
  title: 'Histórico de campanhas 2025',
  sourceType: 'markdown',
  extractedText: CONTEUDO_ATUAL,
  contentLengthBytes: 47,
  indexingStatus: 'Failed',
  indexedAt: null,
  failureReason: '429 do provedor.',
  contentRevision: 1,
  fragmentCount: 0,
  indexingAttempts: 3,
  lastAttemptAt: '2026-09-01T03:14:00Z',
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
};

function file(name: string, content = '# Conteúdo novo'): File {
  return new File([content], name, { type: 'text/plain' });
}

function renderModal(overrides: Partial<Parameters<typeof KnowledgeDocumentModal>[0]> = {}) {
  const handlers = {
    onClose: vi.fn(),
    onCreate: vi.fn().mockResolvedValue(undefined),
    onUpdate: vi.fn().mockResolvedValue(undefined),
  };

  const view = render(
    <MantineProvider theme={theme}>
      <KnowledgeDocumentModal opened document={null} {...handlers} {...overrides} />
    </MantineProvider>,
  );

  return { ...handlers, ...view };
}

// O Modal do Mantine renderiza em PORTAL, fora do `container` devolvido por
// `render()`. Asserção negativa sobre `container.textContent` é VAZIA aqui:
// passa com e sem o defeito, porque nunca enxerga o conteúdo do modal.
//
// Medido na rodada de verificação de guardas (tarefa 8.5): com o aviso
// incondicional do protótipo no lugar, as negativas que liam `container`
// continuaram verdes. Ler `document.body` é o que as torna discriminantes.
function textoVisivel(): string {
  return document.body.textContent ?? '';
}

function fileInput(): HTMLElement {
  return screen.getByTestId('document-dropzone').querySelector('input[type=file]') as HTMLElement;
}

describe('adicionar por arquivo', () => {
  it('cada arquivo vira uma chamada independente ao POST', async () => {
    const user = userEvent.setup();
    const { onCreate } = renderModal();

    await user.upload(fileInput(), [file('primeiro.md', 'um'), file('segundo.txt', 'dois')]);
    await screen.findByDisplayValue('primeiro');

    await user.click(screen.getByRole('button', { name: 'Adicionar 2 documentos' }));

    await waitFor(() => expect(onCreate).toHaveBeenCalledTimes(2));
    expect(onCreate).toHaveBeenNthCalledWith(1, {
      title: 'primeiro',
      sourceType: 'markdown',
      content: 'um',
    });
    expect(onCreate).toHaveBeenNthCalledWith(2, {
      title: 'segundo',
      sourceType: 'markdown',
      content: 'dois',
    });
  });

  it('título vazio bloqueia o envio e não faz chamada nenhuma', async () => {
    const user = userEvent.setup();
    const { onCreate } = renderModal();

    await user.upload(fileInput(), file('primeiro.md'));
    await user.clear(await screen.findByLabelText('Título do documento'));
    await user.click(screen.getByRole('button', { name: 'Adicionar documento' }));

    expect(await screen.findByTestId('modal-error')).toHaveTextContent(
      'Todo documento precisa de um título.',
    );
    expect(onCreate).not.toHaveBeenCalled();
  });

  it('enviar sem arquivo nenhum é bloqueado', async () => {
    const user = userEvent.setup();
    const { onCreate } = renderModal();

    await user.click(screen.getByRole('button', { name: 'Adicionar documento' }));

    expect(await screen.findByTestId('modal-error')).toHaveTextContent(/pelo menos um arquivo/);
    expect(onCreate).not.toHaveBeenCalled();
  });

  it('lote inteiro com sucesso fecha o modal', async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    await user.upload(fileInput(), file('primeiro.md'));
    await screen.findByDisplayValue('primeiro');
    await user.click(screen.getByRole('button', { name: 'Adicionar documento' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  // ASSERÇÃO NEGATIVA (design.md, D2). O protótipo diz "2 documentos serão
  // criados e entram como pendentes", prometendo um resultado de conjunto que o
  // desenho — N chamadas independentes — não sustenta.
  it('o rodapé não promete que os documentos serão criados como conjunto', async () => {
    const user = userEvent.setup();
    renderModal();

    await user.upload(fileInput(), [file('primeiro.md'), file('segundo.txt')]);
    await screen.findByTestId('batch-footer');

    expect(textoVisivel()).not.toMatch(/ser[ãa]o criados/i);
    expect(screen.getByTestId('batch-footer')).toHaveTextContent(
      /os que já entraram continuam criados/i,
    );
  });
});

describe('falha parcial no lote', () => {
  it('falha no meio preserva o que já entrou e mantém o modal aberto', async () => {
    const user = userEvent.setup();
    const onCreate = vi
      .fn()
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce(new Error('Erro 400 ao criar.'))
      .mockResolvedValue(undefined);
    const { onClose } = renderModal({ onCreate });

    await user.upload(fileInput(), [file('primeiro.md'), file('segundo.txt'), file('terceiro.md')]);
    await screen.findByDisplayValue('primeiro');
    await user.click(screen.getByRole('button', { name: 'Adicionar 3 documentos' }));

    await waitFor(() => expect(screen.getByTestId('file-created-0')).toBeInTheDocument());
    expect(screen.getByTestId('file-failed-1')).toHaveTextContent('Erro 400 ao criar.');
    expect(screen.getByTestId('file-created-2')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('reenviar depois de falha parcial não recria os que já entraram', async () => {
    const user = userEvent.setup();
    const onCreate = vi
      .fn()
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce(new Error('Erro 400 ao criar.'))
      .mockResolvedValue(undefined);
    renderModal({ onCreate });

    await user.upload(fileInput(), [file('primeiro.md', 'um'), file('segundo.txt', 'dois')]);
    await screen.findByDisplayValue('primeiro');
    await user.click(screen.getByRole('button', { name: 'Adicionar 2 documentos' }));

    await waitFor(() => expect(screen.getByTestId('file-failed-1')).toBeInTheDocument());
    expect(onCreate).toHaveBeenCalledTimes(2);

    // Segundo acionamento: só a linha que faltava.
    await user.click(screen.getByRole('button', { name: 'Adicionar documento' }));

    await waitFor(() => expect(onCreate).toHaveBeenCalledTimes(3));
    expect(onCreate).toHaveBeenNthCalledWith(3, {
      title: 'segundo',
      sourceType: 'markdown',
      content: 'dois',
    });
    // A primeira nunca foi reenviada.
    expect(
      onCreate.mock.calls.filter((c) => (c[0] as { title: string }).title === 'primeiro'),
    ).toHaveLength(1);
  });
});

describe('adicionar escrevendo manualmente', () => {
  it('cria com título, conteúdo e tipo de origem markdown', async () => {
    const user = userEvent.setup();
    const { onCreate } = renderModal();

    await user.click(screen.getByRole('radio', { name: 'Escrever manualmente' }));
    await user.type(screen.getByLabelText(/Título/), 'Prazos de ativação');
    await user.type(screen.getByLabelText('Conteúdo (markdown)'), '# Ativação');
    await user.click(screen.getByRole('button', { name: 'Adicionar documento' }));

    await waitFor(() =>
      expect(onCreate).toHaveBeenCalledWith({
        title: 'Prazos de ativação',
        sourceType: 'markdown',
        content: '# Ativação',
      }),
    );
  });

  it('tipo de origem aparece desabilitado com markdown', async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole('radio', { name: 'Escrever manualmente' }));

    // O Select do Mantine monta dois elementos rotulados: o input visível e o
    // input nativo escondido que carrega o valor do formulário.
    const [visivel] = screen.getAllByLabelText('Tipo de origem');
    expect(visivel).toBeDisabled();
    expect(visivel).toHaveValue('markdown');
  });

  it('título vazio bloqueia a criação manual', async () => {
    const user = userEvent.setup();
    const { onCreate } = renderModal();

    await user.click(screen.getByRole('radio', { name: 'Escrever manualmente' }));
    await user.click(screen.getByRole('button', { name: 'Adicionar documento' }));

    expect(await screen.findByText('O título do documento é obrigatório.')).toBeInTheDocument();
    expect(onCreate).not.toHaveBeenCalled();
  });
});

describe('atualizar documento', () => {
  it('abre no modo manual com título e conteúdo atuais', () => {
    renderModal({ document: documento });

    expect(screen.getByLabelText(/Título/)).toHaveValue('Histórico de campanhas 2025');
    expect(screen.getByLabelText('Conteúdo (markdown)')).toHaveValue(CONTEUDO_ATUAL);
  });

  it('substituir por arquivo envia o conteúdo novo e preserva o título atual', async () => {
    const user = userEvent.setup();
    const { onUpdate } = renderModal({ document: documento });

    await user.click(screen.getByRole('radio', { name: 'Substituir por arquivo' }));
    await user.upload(fileInput(), file('outro-nome.md', '# Conteúdo substituído'));
    await screen.findByText('outro-nome.md');
    await user.click(screen.getByRole('button', { name: /Salvar/ }));

    await waitFor(() =>
      expect(onUpdate).toHaveBeenCalledWith({
        title: 'Histórico de campanhas 2025',
        sourceType: 'markdown',
        content: '# Conteúdo substituído',
      }),
    );
  });

  it('conteúdo alterado avisa que volta para pendente e a ação indica reindexação', async () => {
    const user = userEvent.setup();
    renderModal({ document: documento });

    await user.type(screen.getByLabelText('Conteúdo (markdown)'), ' mais texto');

    expect(screen.getByTestId('update-reindex-warning')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Salvar e reindexar' })).toBeInTheDocument();
  });

  // POSITIVO. A garantia 3 de D9 da etapa 1 chegando à tela: o comportamento
  // real é MELHOR que o aviso do protótipo, e o operador usa isso para decidir
  // se atualiza agora ou depois.
  it('o aviso diz que o conteúdo anterior continua respondendo, inclusive se a nova indexação falhar', async () => {
    const user = userEvent.setup();
    renderModal({ document: documento });

    await user.type(screen.getByLabelText('Conteúdo (markdown)'), ' mais texto');

    const aviso = screen.getByTestId('update-reindex-warning');
    expect(aviso).toHaveTextContent(/continua respondendo até a nova indexação terminar/i);
    expect(aviso).toHaveTextContent(/se ela falhar, ele permanece/i);
  });

  // POSITIVO. Sem esta frase o operador que corrige só o título espera uma
  // reindexação que não vem, vê o estado não mudar e lê isso como defeito. Só a
  // asserção negativa abaixo passaria com a tela calada.
  it('alterar só o título informa que salvar não reindexa', async () => {
    const user = userEvent.setup();
    renderModal({ document: documento });

    await user.type(screen.getByLabelText(/Título/), ' revisado');

    expect(screen.getByTestId('update-no-reindex-note')).toHaveTextContent(
      /salvar não reindexa o documento/i,
    );
  });

  // NEGATIVO.
  it('alterar só o título não menciona reindexar na ação de salvar', async () => {
    const user = userEvent.setup();
    renderModal({ document: documento });

    await user.type(screen.getByLabelText(/Título/), ' revisado');

    expect(screen.getByRole('button', { name: 'Salvar' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Salvar e reindexar' })).not.toBeInTheDocument();
    expect(screen.queryByTestId('update-reindex-warning')).not.toBeInTheDocument();
  });

  // NEGATIVO. As duas afirmações falsas do protótipo, conferidas contra
  // KnowledgeIndexingService: os fragmentos antigos NÃO saem das consultas e NÃO
  // são descartados no momento do salvamento.
  it('o aviso não afirma que o documento sai das consultas nem descarte imediato', async () => {
    const user = userEvent.setup();
    renderModal({ document: documento });

    await user.type(screen.getByLabelText('Conteúdo (markdown)'), ' mais texto');

    expect(textoVisivel()).not.toMatch(/sai das consultas/i);
    expect(textoVisivel()).not.toMatch(/fragmentos antigos são descartados/i);
  });
});

describe('cópia que o sistema não sustenta', () => {
  // NEGATIVO (convenção 13). Nada em apps/workers correlaciona tamanho com falha
  // de embedding, e o conselho do protótipo não vira nem texto estático de ajuda.
  it('nenhuma cópia relaciona tamanho de documento a falha de indexação', () => {
    renderModal();

    expect(textoVisivel()).not.toMatch(/grandes?\b/i);
    expect(textoVisivel()).not.toMatch(/limite de requisi/i);
  });

  // NEGATIVO (convenção 13). O sistema registra o que a busca devolveu, não o
  // que a resposta usou.
  it('nada afirma que uma resposta foi fundamentada num trecho', () => {
    renderModal({ document: documento });

    expect(textoVisivel()).not.toMatch(/fundamentad/i);
    expect(textoVisivel()).not.toMatch(/\bfonte\b/i);
  });
});
