import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { FolderPickerModal, type FolderPickerModalProps } from './FolderPickerModal';
import { buildFoldersInUse } from '../utils/foldersInUse';
import type { ConnectorFolder } from '../types/connectors';
import type { KnowledgeBase } from '../types/knowledgeBase';

const EMAIL = 'buteco-sync@projeto.iam.gserviceaccount.com';

function folder(
  id: string,
  name: string,
  kind: ConnectorFolder['kind'] = 'Folder',
): ConnectorFolder {
  return { id, name, kind, webUrl: `https://drive.google.com/drive/folders/${id}` };
}

const suporte = folder('d-suporte', 'Suporte', 'SharedDrive');
const guia = folder('f-guia', 'Guia do produto');
const faq = folder('f-faq', 'FAQ Suporte');
const rh = folder('f-rh', 'Políticas RH');

const rhBase: KnowledgeBase = {
  id: 'b-rh',
  name: 'Políticas internas de RH',
  description: 'd',
  isActive: false,
  createdAt: '2026-10-01T00:00:00Z',
  updatedAt: '2026-10-01T00:00:00Z',
  contentMode: 'Synced',
  syncSource: {
    provider: 'google-drive',
    folderId: 'f-rh',
    folderName: 'Políticas RH',
    folderUrl: 'https://x',
  },
  syncState: {
    lastCompletedAt: null,
    lastFinishedAt: null,
    failingSince: null,
    lastError: null,
    ignoredFiles: null,
  },
};

function renderModal(props: Partial<FolderPickerModalProps> = {}) {
  const handlers = {
    onClose: vi.fn(),
    onOpenFolder: vi.fn(),
    onGoTo: vi.fn(),
    onReload: vi.fn(),
    onSelect: vi.fn(),
    onConfirm: vi.fn(),
  };
  render(
    <MantineProvider theme={theme}>
      <FolderPickerModal
        opened
        providerKey="google-drive"
        accountEmail={EMAIL}
        path={[]}
        level={{ status: 'success', folders: [] }}
        foldersInUse={new Map()}
        basesUnavailable={false}
        selected={null}
        {...handlers}
        {...props}
      />
    </MantineProvider>,
  );
  return handlers;
}

function dialog() {
  return screen.getByRole('dialog', { name: 'Escolher pasta' });
}

function row(name: string) {
  return screen.getByTestId(`folder-row-${name}`);
}

describe('FolderPickerModal — nível de cima', () => {
  it('agrupa Drives compartilhados e pastas compartilhadas com a conta', () => {
    renderModal({ level: { status: 'success', folders: [guia, suporte] } });

    const drives = screen.getByRole('list', { name: 'Drives compartilhados' });
    const shared = screen.getByRole('list', { name: 'Pastas compartilhadas com a conta' });
    expect(within(drives).getByText('Suporte')).toBeInTheDocument();
    expect(within(shared).getByText('Guia do produto')).toBeInTheDocument();
  });

  it('Drive compartilhado só abre; pasta compartilhada pode ser escolhida e aberta', async () => {
    const user = userEvent.setup();
    const { onOpenFolder, onSelect } = renderModal({
      level: { status: 'success', folders: [guia, suporte] },
    });

    expect(within(row('Suporte')).queryByRole('radio')).not.toBeInTheDocument();
    await user.click(within(row('Suporte')).getByRole('button', { name: 'Abrir Suporte' }));
    expect(onOpenFolder).toHaveBeenCalledWith(suporte);

    await user.click(
      within(row('Guia do produto')).getByRole('radio', { name: 'Selecionar Guia do produto' }),
    );
    expect(onSelect).toHaveBeenCalledWith(guia);
    expect(
      within(row('Guia do produto')).getByRole('button', { name: 'Abrir Guia do produto' }),
    ).toBeInTheDocument();
  });

  it('nada compartilhado: diz isso com o e-mail, sem texto de erro', () => {
    renderModal({ level: { status: 'success', folders: [] } });

    expect(dialog()).toHaveTextContent(`Nada foi compartilhado com ${EMAIL} ainda.`);
    expect(dialog().textContent).not.toMatch(/Não foi possível|erro|fora do ar/i);
  });

  it('caminho mostra só "Início"', () => {
    renderModal();
    expect(screen.getByRole('navigation', { name: 'Caminho' })).toHaveTextContent(/^Início$/);
  });
});

describe('FolderPickerModal — dentro de uma pasta', () => {
  const path = [
    { id: 'd-suporte', name: 'Suporte' },
    { id: 'f-doc', name: 'Documentação' },
  ];

  it('mostra o caminho a partir de Início, e cada passo volta àquele nível', async () => {
    const user = userEvent.setup();
    const { onGoTo } = renderModal({ path, level: { status: 'success', folders: [faq] } });

    const nav = screen.getByRole('navigation', { name: 'Caminho' });
    expect(nav).toHaveTextContent('Início›Suporte›Documentação');
    await user.click(within(nav).getByRole('button', { name: 'Suporte' }));
    expect(onGoTo).toHaveBeenCalledWith(1);
    await user.click(within(nav).getByRole('button', { name: 'Início' }));
    expect(onGoTo).toHaveBeenCalledWith(0);
    expect(within(nav).queryByRole('button', { name: 'Documentação' })).not.toBeInTheDocument();
  });

  it('mostra a contagem devolvida', () => {
    renderModal({ path, level: { status: 'success', folders: [faq, guia, rh] } });
    expect(dialog()).toHaveTextContent('3 pastas');
  });

  it('uma pasta, no singular', () => {
    renderModal({ path, level: { status: 'success', folders: [faq] } });
    expect(dialog()).toHaveTextContent('1 pasta');
    expect(dialog()).not.toHaveTextContent('1 pastas');
  });

  it('sem subpastas: diz como fato, sem erro e sem "0 pastas"', () => {
    renderModal({ path, level: { status: 'success', folders: [] } });

    expect(dialog()).toHaveTextContent('Esta pasta não tem subpastas.');
    expect(dialog()).not.toHaveTextContent('0 pastas');
    expect(dialog().textContent).not.toMatch(/Não foi possível|erro|fora do ar/i);
  });
});

describe('FolderPickerModal — carregando e erro', () => {
  it('carregando: indica carregamento e não afirma erro nem vazio', () => {
    renderModal({ level: { status: 'loading' } });

    expect(dialog()).toHaveTextContent('Carregando pastas…');
    expect(dialog().textContent).not.toMatch(
      /Não foi possível|erro|fora do ar|sem acesso|Nenhuma|Nada foi/i,
    );
  });

  it('erro: mostra o texto e "Tentar de novo", sem lista vazia', async () => {
    const user = userEvent.setup();
    const { onReload } = renderModal({
      path: [{ id: 'p', name: 'Pasta' }],
      level: { status: 'error', message: 'A conta x não tem acesso a esta pasta.' },
    });

    expect(within(dialog()).getByRole('alert')).toHaveTextContent(
      'A conta x não tem acesso a esta pasta.',
    );
    expect(dialog()).not.toHaveTextContent('Esta pasta não tem subpastas.');
    await user.click(screen.getByRole('button', { name: 'Tentar de novo' }));
    expect(onReload).toHaveBeenCalled();
  });

  it('"Recarregar" refaz o nível atual', async () => {
    const user = userEvent.setup();
    const { onReload } = renderModal({ level: { status: 'success', folders: [guia] } });

    expect(dialog()).toHaveTextContent(`Só aparece o que foi compartilhado com ${EMAIL}.`);
    await user.click(screen.getByRole('button', { name: 'Recarregar' }));
    expect(onReload).toHaveBeenCalled();
  });
});

describe('FolderPickerModal — pasta em uso', () => {
  it('desabilita a escolha, nomeia a base sem link, e ainda deixa abrir', async () => {
    const user = userEvent.setup();
    const { onOpenFolder } = renderModal({
      path: [{ id: 'd-suporte', name: 'Suporte' }],
      level: { status: 'success', folders: [faq, rh] },
      foldersInUse: buildFoldersInUse([rhBase]),
    });

    const inUse = row('Políticas RH');
    expect(within(inUse).getByRole('radio')).toBeDisabled();
    expect(inUse).toHaveTextContent('Já sincronizada pela base “Políticas internas de RH”');
    expect(within(inUse).queryByRole('link')).not.toBeInTheDocument();
    await user.click(within(inUse).getByRole('button', { name: 'Abrir Políticas RH' }));
    expect(onOpenFolder).toHaveBeenCalledWith(rh);
    expect(within(row('FAQ Suporte')).getByRole('radio')).toBeEnabled();
  });

  it('listagem de bases indisponível: avisa, e nenhuma pasta fica desabilitada por uso', () => {
    renderModal({
      path: [{ id: 'd-suporte', name: 'Suporte' }],
      level: { status: 'success', folders: [faq, rh] },
      foldersInUse: new Map(),
      basesUnavailable: true,
    });

    expect(dialog()).toHaveTextContent(
      'Não foi possível conferir quais pastas já são usadas por outras bases. O cadastro confere ao criar.',
    );
    expect(within(row('Políticas RH')).getByRole('radio')).toBeEnabled();
    expect(dialog()).not.toHaveTextContent('Já sincronizada');
  });
});

describe('FolderPickerModal — confirmar', () => {
  it('sem escolha, "Selecionar pasta" fica desabilitado', () => {
    renderModal({ level: { status: 'success', folders: [guia] } });
    expect(screen.getByRole('button', { name: 'Selecionar pasta' })).toBeDisabled();
  });

  it('com escolha, nomeia a pasta no botão e no rodapé, e confirma', async () => {
    const user = userEvent.setup();
    const { onConfirm } = renderModal({
      path: [{ id: 'd-suporte', name: 'Suporte' }],
      level: { status: 'success', folders: [faq] },
      selected: faq,
    });

    expect(within(row('FAQ Suporte')).getByRole('radio')).toBeChecked();
    expect(dialog()).toHaveTextContent('Os arquivos da raiz de “FAQ Suporte” entram na base.');
    await user.click(screen.getByRole('button', { name: 'Selecionar “FAQ Suporte”' }));
    expect(onConfirm).toHaveBeenCalled();
  });
});
