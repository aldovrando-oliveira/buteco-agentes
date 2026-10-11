import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import {
  KnowledgeBaseDeleteModal,
  type KnowledgeBaseDeleteModalProps,
} from './KnowledgeBaseDeleteModal';

function renderModal(overrides: Partial<KnowledgeBaseDeleteModalProps> = {}) {
  const props: KnowledgeBaseDeleteModalProps = {
    opened: true,
    knowledgeBaseName: 'Políticas de Cobrança',
    documentCount: 3,
    agentNames: ['Atendente de Suporte', 'Triagem'],
    syncedFolder: null,
    pending: false,
    failure: null,
    onConfirm: vi.fn(),
    onClose: vi.fn(),
    ...overrides,
  };
  render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <KnowledgeBaseDeleteModal {...props} />
      </MemoryRouter>
    </MantineProvider>,
  );
  return { props, dialog: screen.getByRole('dialog') };
}

// As frases da D2 do design.md da change exclusao-base-painel.
describe('KnowledgeBaseDeleteModal — o que vai junto', () => {
  it('nomeia a base no título, a quantidade de documentos, os agentes e que não há volta', () => {
    const { dialog } = renderModal();

    expect(screen.getByRole('dialog', { name: 'Excluir "Políticas de Cobrança"?' })).toBe(dialog);
    expect(dialog).toHaveTextContent('A base e os seus 3 documentos são excluídos');
    expect(dialog).toHaveTextContent('Não há como desfazer.');
    expect(dialog).toHaveTextContent('Os vínculos com Atendente de Suporte e Triagem saem junto.');
    expect(dialog).toHaveTextContent('já não consultam esta base, porque ela está inativa');
  });

  it('um documento e um agente, no singular', () => {
    const { dialog } = renderModal({ documentCount: 1, agentNames: ['Triagem'] });

    expect(dialog).toHaveTextContent('A base e o único documento dela são excluídos');
    expect(dialog).toHaveTextContent('O vínculo com Triagem sai junto.');
    expect(dialog).not.toHaveTextContent('documentos são excluídos');
  });

  it('listagem vazia e nenhum agente: diz que não há, e nenhum número', () => {
    const { dialog } = renderModal({ documentCount: 0, agentNames: [] });

    expect(dialog).toHaveTextContent('Ela não tem documentos.');
    expect(dialog).toHaveTextContent('Nenhum agente está vinculado a esta base.');
    expect(dialog.textContent).not.toMatch(/\d/);
  });

  it('documentos e agentes desconhecidos não viram zero nem "nenhum"', () => {
    const { dialog } = renderModal({ documentCount: null, agentNames: null });

    expect(dialog).toHaveTextContent('A base e todos os documentos dela são excluídos');
    expect(dialog).toHaveTextContent('Não foi possível conferir os agentes vinculados');
    // A ausência é o que importa (convenção 13): nenhum número de documentos, e
    // nada que leia como "não há agentes" nem "não há documentos".
    expect(dialog.textContent).not.toMatch(/\d/);
    expect(dialog).not.toHaveTextContent(/Nenhum agente/i);
    expect(dialog).not.toHaveTextContent(/não tem documentos/i);
  });

  it('base sincronizada: a pasta fica livre e o Drive não é tocado', () => {
    const { dialog } = renderModal({
      syncedFolder: { folderName: 'Políticas', providerLabel: 'Google Drive' },
    });

    expect(dialog).toHaveTextContent(
      'A pasta “Políticas” fica livre para ser usada por outra base. Os arquivos no Google Drive não são tocados.',
    );
  });

  it('base manual não fala de pasta nem de Drive', () => {
    const { dialog } = renderModal({ syncedFolder: null });

    expect(dialog).not.toHaveTextContent(/pasta/i);
    expect(dialog).not.toHaveTextContent(/Drive/);
  });
});

describe('KnowledgeBaseDeleteModal — botões e fechamento', () => {
  it('Excluir base chama a confirmação', async () => {
    const user = userEvent.setup();
    const { props, dialog } = renderModal();

    await user.click(within(dialog).getByRole('button', { name: 'Excluir base' }));

    expect(props.onConfirm).toHaveBeenCalledTimes(1);
  });

  it('Cancelar e Esc só fecham, sem confirmar', async () => {
    const user = userEvent.setup();
    const { props, dialog } = renderModal();

    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await user.keyboard('{Escape}');

    expect(props.onClose).toHaveBeenCalledTimes(2);
    expect(props.onConfirm).not.toHaveBeenCalled();
  });

  it('durante a requisição o modal não fecha', async () => {
    const user = userEvent.setup();
    const { props, dialog } = renderModal({ pending: true });

    await user.keyboard('{Escape}');

    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(props.onClose).not.toHaveBeenCalled();
  });
});

describe('KnowledgeBaseDeleteModal — falha da requisição', () => {
  it('falha que não se repete: mostra o texto, esconde Excluir base, oferece Fechar', () => {
    const { dialog } = renderModal({
      failure: { kind: 'active', message: 'TEXTO-DA-FALHA', canRetry: false },
    });

    expect(within(dialog).getByRole('alert')).toHaveTextContent('TEXTO-DA-FALHA');
    expect(within(dialog).queryByRole('button', { name: 'Excluir base' })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Fechar' })).toBeInTheDocument();
  });

  it('falha que se repete mantém Excluir base', () => {
    const { dialog } = renderModal({
      failure: { kind: 'contended', message: 'TEXTO-DA-FALHA', canRetry: true },
    });

    expect(within(dialog).getByRole('alert')).toHaveTextContent('TEXTO-DA-FALHA');
    expect(within(dialog).getByRole('button', { name: 'Excluir base' })).toBeInTheDocument();
  });

  it('base já excluída oferece a volta para a listagem', () => {
    const { dialog } = renderModal({
      failure: { kind: 'not-found', message: 'TEXTO-DA-FALHA', canRetry: false },
    });

    expect(within(dialog).getByRole('link', { name: 'Voltar para as bases' })).toHaveAttribute(
      'href',
      '/knowledge-bases',
    );
    expect(within(dialog).queryByRole('button', { name: 'Excluir base' })).not.toBeInTheDocument();
  });

  it('sem falha, nenhum alerta', () => {
    const { dialog } = renderModal();

    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
    expect(
      within(dialog).queryByRole('link', { name: 'Voltar para as bases' }),
    ).not.toBeInTheDocument();
  });
});
