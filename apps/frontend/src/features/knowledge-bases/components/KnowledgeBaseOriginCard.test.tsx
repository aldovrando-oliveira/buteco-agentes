import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import {
  KnowledgeBaseOriginCard,
  type KnowledgeBaseOriginCardProps,
} from './KnowledgeBaseOriginCard';

const EMAIL = 'buteco-sync@projeto.iam.gserviceaccount.com';
const ERROR_WORDS = /Não foi possível|erro|fora do ar|sem acesso/i;

const folder = {
  id: 'f-faq',
  name: 'FAQ Suporte',
  kind: 'Folder' as const,
  webUrl: 'https://drive.google.com/drive/folders/f-faq',
};

function renderCard(props: Partial<KnowledgeBaseOriginCardProps> = {}) {
  const handlers = {
    onModeChange: vi.fn(),
    onProviderChange: vi.fn(),
    onChooseFolder: vi.fn(),
  };
  render(
    <MantineProvider theme={theme}>
      <KnowledgeBaseOriginCard
        mode="Manual"
        syncAvailable
        providers={{ status: 'loading' }}
        providerKey={null}
        folder={null}
        {...handlers}
        {...props}
      />
    </MantineProvider>,
  );
  return handlers;
}

function card() {
  return screen.getByRole('group', { name: /Origem dos documentos/ });
}

describe('KnowledgeBaseOriginCard — escolha da origem', () => {
  it('mostra as duas opções com Manual marcada e o aviso de que a origem não muda', () => {
    renderCard();

    expect(screen.getByRole('radio', { name: /Manual/ })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: /Sincronizada/ })).toHaveAttribute(
      'aria-checked',
      'false',
    );
    expect(card()).toHaveTextContent('Não pode ser alterada depois de criar a base.');
  });

  it('com Manual marcada, não mostra nada da origem sincronizada', () => {
    renderCard();

    expect(screen.queryByText(/Provedor/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Pasta/)).not.toBeInTheDocument();
  });

  it('marcar Sincronizada avisa quem monta o card', async () => {
    const user = userEvent.setup();
    const { onModeChange } = renderCard();

    await user.click(screen.getByRole('radio', { name: /Sincronizada/ }));

    expect(onModeChange).toHaveBeenCalledWith('Synced');
  });

  it('sem o endereço do apps/connectors, Sincronizada fica desabilitada e explicada', async () => {
    const user = userEvent.setup();
    const { onModeChange } = renderCard({ syncAvailable: false });

    const synced = screen.getByRole('radio', { name: /Sincronizada/ });
    expect(synced).toBeDisabled();
    expect(card()).toHaveTextContent(
      'A sincronização com pastas não está habilitada neste painel.',
    );
    await user.click(synced);
    expect(onModeChange).not.toHaveBeenCalled();
    expect(screen.getByRole('radio', { name: /Manual/ })).toHaveAttribute('aria-checked', 'true');
  });
});

describe('KnowledgeBaseOriginCard — provedores', () => {
  it('carregando: indica carregamento e não contém texto de erro', () => {
    renderCard({ mode: 'Synced', providers: { status: 'loading' } });

    expect(card()).toHaveTextContent('Carregando provedores…');
    expect(card().textContent).not.toMatch(ERROR_WORDS);
  });

  it('erro: mostra o texto e "Tentar de novo", sem seletor de provedor', async () => {
    const user = userEvent.setup();
    const onRetry = vi.fn();
    renderCard({
      mode: 'Synced',
      providers: {
        status: 'error',
        message: 'Não foi possível falar com o serviço de conectores.',
        onRetry,
      },
    });

    expect(card()).toHaveTextContent('Não foi possível falar com o serviço de conectores.');
    expect(screen.queryByRole('combobox', { name: /Provedor/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Tentar de novo' }));
    expect(onRetry).toHaveBeenCalled();
  });

  it('nenhum provedor: explica, sem texto de erro e sem botão de escolher pasta', () => {
    renderCard({ mode: 'Synced', providers: { status: 'success', providers: [] } });

    expect(card()).toHaveTextContent(
      'Nenhum provedor está configurado no serviço de conectores desta instalação.',
    );
    expect(card().textContent).not.toMatch(ERROR_WORDS);
    expect(screen.queryByRole('button', { name: 'Escolher pasta' })).not.toBeInTheDocument();
  });

  it('provedor escolhido: nome de exibição, e-mail com copiar e a instrução de Leitor', () => {
    renderCard({
      mode: 'Synced',
      providers: { status: 'success', providers: [{ key: 'google-drive', accountEmail: EMAIL }] },
      providerKey: 'google-drive',
    });

    expect(screen.getByRole('combobox', { name: /Provedor/ })).toHaveValue('Google Drive');
    expect(card()).toHaveTextContent(EMAIL);
    expect(card()).toHaveTextContent(/como Leitor/);
    expect(screen.getByRole('button', { name: 'Copiar' })).toBeInTheDocument();
  });
});

describe('KnowledgeBaseOriginCard — pasta', () => {
  const synced: Partial<KnowledgeBaseOriginCardProps> = {
    mode: 'Synced',
    providers: { status: 'success', providers: [{ key: 'google-drive', accountEmail: EMAIL }] },
    providerKey: 'google-drive',
  };

  it('sem pasta: diz que nenhuma foi escolhida e oferece escolher', async () => {
    const user = userEvent.setup();
    const { onChooseFolder } = renderCard(synced);

    expect(card()).toHaveTextContent('Nenhuma pasta escolhida');
    expect(card()).toHaveTextContent(
      'Entram só os arquivos da raiz da pasta: Google Docs e arquivos .md.',
    );
    await user.click(screen.getByRole('button', { name: 'Escolher pasta' }));
    expect(onChooseFolder).toHaveBeenCalled();
  });

  it('com pasta: nome, "Abrir no Drive" em outra aba e "Trocar pasta"', () => {
    renderCard({ ...synced, folder });

    expect(card()).toHaveTextContent('FAQ Suporte');
    const link = screen.getByRole('link', { name: /Abrir no Drive/ });
    expect(link).toHaveAttribute('href', folder.webUrl);
    expect(link).toHaveAttribute('target', '_blank');
    expect(link.getAttribute('rel')).toMatch(/noopener/);
    expect(screen.getByRole('button', { name: 'Trocar pasta' })).toBeInTheDocument();
    expect(card()).not.toHaveTextContent('Nenhuma pasta escolhida');
  });

  it('erro de campo da pasta aparece no campo', () => {
    renderCard({ ...synced, folderError: 'Escolha a pasta que a base vai acompanhar.' });

    expect(card()).toHaveTextContent('Escolha a pasta que a base vai acompanhar.');
  });

  it('erro do cadastro aparece no card, num alerta', () => {
    renderCard({ ...synced, folder, submitError: 'Esta pasta já é usada pela base “RH”.' });

    const alert = within(card()).getByRole('alert');
    expect(alert).toHaveTextContent('Esta pasta já é usada pela base “RH”.');
  });
});
