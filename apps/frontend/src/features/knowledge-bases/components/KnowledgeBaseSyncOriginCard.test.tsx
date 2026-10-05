import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { KnowledgeBaseSyncOriginCard, type SyncRequestView } from './KnowledgeBaseSyncOriginCard';
import { formatSyncInstant } from '../utils/syncState';
import type { KnowledgeBaseSyncSource, KnowledgeBaseSyncState } from '../types/knowledgeBase';

const EMAIL = 'sync@exemplo.iam.gserviceaccount.com';
const T_DONE = '2026-09-26T17:32:00Z';
const T_FAIL = '2026-09-27T12:10:00Z';

const SOURCE: KnowledgeBaseSyncSource = {
  provider: 'google-drive',
  folderId: 'f-faq',
  folderName: 'FAQ Suporte',
  folderUrl: 'https://drive.google.com/drive/folders/f-faq',
};

const NEVER: KnowledgeBaseSyncState = {
  lastCompletedAt: null,
  lastFinishedAt: null,
  failingSince: null,
  lastError: null,
  ignoredFiles: null,
};

const UP_TO_DATE: KnowledgeBaseSyncState = {
  ...NEVER,
  lastCompletedAt: T_DONE,
  lastFinishedAt: T_DONE,
  ignoredFiles: [],
};

const FAILING: KnowledgeBaseSyncState = {
  ...UP_TO_DATE,
  lastFinishedAt: T_FAIL,
  failingSince: T_FAIL,
  lastError: { code: 'access-denied', detail: EMAIL },
};

const FAILING_NEVER_COMPLETED: KnowledgeBaseSyncState = {
  ...NEVER,
  lastFinishedAt: T_FAIL,
  failingSince: T_FAIL,
  lastError: { code: 'rate-limited', detail: null },
};

function renderCard(
  props: Partial<{
    syncSource: KnowledgeBaseSyncSource;
    syncState: KnowledgeBaseSyncState;
    syncAvailable: boolean;
    request: SyncRequestView;
  }> = {},
) {
  const onSync = vi.fn();
  render(
    <MantineProvider theme={theme}>
      <KnowledgeBaseSyncOriginCard
        syncSource={props.syncSource ?? SOURCE}
        syncState={props.syncState ?? UP_TO_DATE}
        syncAvailable={props.syncAvailable ?? true}
        request={props.request ?? { phase: 'idle' }}
        onSync={onSync}
      />
    </MantineProvider>,
  );
  return { onSync, card: screen.getByTestId('sync-origin-card') };
}

describe('origem', () => {
  it('em dia: pasta, link, provedor, última concluída e sem erros no último ciclo', () => {
    const { card } = renderCard();

    expect(within(card).getByText('Origem — pasta sincronizada')).toBeInTheDocument();
    expect(within(card).getByText('FAQ Suporte')).toBeInTheDocument();
    const link = within(card).getByRole('link', { name: /Abrir no Google Drive/ });
    expect(link).toHaveAttribute('href', SOURCE.folderUrl);
    expect(link).toHaveAttribute('target', '_blank');
    expect(within(card).getByText('Google Drive')).toBeInTheDocument();
    expect(within(card).getByText(formatSyncInstant(T_DONE))).toBeInTheDocument();
    expect(within(card).getByText('Sem erros no último ciclo')).toBeInTheDocument();
    expect(within(card).getByText('Pasta')).toBeInTheDocument();
    expect(screen.queryByTestId('sync-failure-alert')).not.toBeInTheDocument();
  });

  it('nunca sincronizou: espera em tom neutro, sem falha', () => {
    const { card } = renderCard({ syncState: NEVER });

    expect(within(card).getByText('Nenhuma ainda')).toBeInTheDocument();
    expect(
      within(card).getByText(
        'A base entra na próxima rodada de sincronização, que roda a cada 5 minutos.',
      ),
    ).toBeInTheDocument();
    expect(card).not.toHaveTextContent(/falha/i);
    expect(card).not.toHaveTextContent('Sem erros no último ciclo');
    expect(screen.queryByTestId('sync-failure-alert')).not.toBeInTheDocument();
  });

  it('falhando depois de concluir: rótulo da última concluída, falhando desde e alerta', () => {
    const { card } = renderCard({ syncState: FAILING });

    expect(
      within(card).getByText('Pasta — nome na última sincronização concluída'),
    ).toBeInTheDocument();
    expect(within(card).getByText(formatSyncInstant(T_DONE))).toBeInTheDocument();
    expect(
      within(card).getByText(`Falhando desde ${formatSyncInstant(T_FAIL)}`),
    ).toBeInTheDocument();
    expect(card).not.toHaveTextContent('Sem erros no último ciclo');

    const alert = screen.getByTestId('sync-failure-alert');
    expect(alert).toHaveTextContent('Sem acesso à pasta');
    expect(alert).toHaveTextContent(EMAIL);
    expect(alert).toHaveTextContent('compartilhe a pasta com essa conta como Leitor');
    expect(alert).toHaveTextContent('Nenhum documento saiu da base');
  });

  it('falhando sem nunca ter concluído: nome no cadastro, e não o da última concluída', () => {
    const { card } = renderCard({ syncState: FAILING_NEVER_COMPLETED });

    expect(within(card).getByText('Pasta — nome no cadastro')).toBeInTheDocument();
    expect(
      within(card).queryByText('Pasta — nome na última sincronização concluída'),
    ).not.toBeInTheDocument();
    expect(within(card).getByText('Nenhuma ainda')).toBeInTheDocument();
    expect(
      within(card).getByText(`Falhando desde ${formatSyncInstant(T_FAIL)}`),
    ).toBeInTheDocument();
    expect(screen.getByTestId('sync-failure-alert')).toHaveTextContent(
      'nenhuma sincronização foi concluída ainda',
    );
  });

  it('provedor desconhecido aparece pela chave, e o link não nomeia o Google Drive', () => {
    const { card } = renderCard({ syncSource: { ...SOURCE, provider: 'onedrive' } });

    expect(within(card).getByText('onedrive')).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: /Abrir a pasta/ })).toBeInTheDocument();
    expect(card).not.toHaveTextContent('Google Drive');
  });

  it('nenhum texto do card manda excluir', () => {
    const { card } = renderCard({ syncState: FAILING });
    expect(card).not.toHaveTextContent(/exclu|remov|apag/i);
  });
});

describe('Sincronizar agora', () => {
  it('em dia, rótulo "Sincronizar agora"; em falha, "Tentar sincronizar agora"', async () => {
    const user = userEvent.setup();
    const { onSync } = renderCard();

    await user.click(screen.getByRole('button', { name: 'Sincronizar agora' }));
    expect(onSync).toHaveBeenCalledTimes(1);
  });

  it('em falha, o rótulo é "Tentar sincronizar agora"', () => {
    renderCard({ syncState: FAILING });
    expect(screen.getByRole('button', { name: 'Tentar sincronizar agora' })).toBeInTheDocument();
  });

  it('sem o endereço do apps/connectors, botão desabilitado com explicação, e o estado continua', () => {
    renderCard({ syncState: FAILING, syncAvailable: false });

    expect(screen.getByRole('button', { name: 'Tentar sincronizar agora' })).toBeDisabled();
    expect(screen.getByTestId('sync-unavailable')).toHaveTextContent(
      'A sincronização manual não está habilitada neste painel',
    );
    expect(screen.getByTestId('sync-failure-alert')).toHaveTextContent(EMAIL);
  });

  it('solicitada: aguardando, botão desabilitado, e nenhum texto de conclusão', () => {
    renderCard({ request: { phase: 'waiting' } });

    expect(screen.getByRole('button', { name: 'Sincronizar agora' })).toBeDisabled();
    const status = screen.getByTestId('sync-request-status');
    expect(status).toHaveTextContent('Sincronização solicitada. Aguardando o resultado…');
    expect(screen.getByTestId('sync-origin-card')).not.toHaveTextContent(/concluída em|terminou/);
  });

  it('concluída: data da última sincronização concluída', () => {
    renderCard({ request: { phase: 'finished' } });

    expect(screen.getByTestId('sync-request-status')).toHaveTextContent(
      `Sincronização concluída em ${formatSyncInstant(T_DONE)}.`,
    );
    expect(screen.getByRole('button', { name: 'Sincronizar agora' })).toBeEnabled();
  });

  it('terminada com falha: diz que terminou com falha, sem conclusão bem-sucedida', () => {
    renderCard({ syncState: FAILING, request: { phase: 'finished' } });

    const status = screen.getByTestId('sync-request-status');
    expect(status).toHaveTextContent('A sincronização terminou com falha.');
    expect(status).not.toHaveTextContent('concluída em');
  });

  it('sem resultado no limite: não afirma sucesso nem falha', () => {
    renderCard({ request: { phase: 'timed-out' } });

    const status = screen.getByTestId('sync-request-status');
    expect(status).toHaveTextContent('Nenhum resultado foi gravado em 5 minutos.');
    expect(status).not.toHaveTextContent(/concluída em|terminou com falha/);
    expect(screen.getByRole('button', { name: 'Sincronizar agora' })).toBeEnabled();
  });

  it('pedindo: botão em carregamento', () => {
    renderCard({ request: { phase: 'starting' } });
    expect(screen.getByRole('button', { name: 'Sincronizar agora' })).toHaveAttribute(
      'data-loading',
      'true',
    );
  });

  it('erro do pedido: texto persistente e botão de volta', () => {
    renderCard({ request: { phase: 'error', message: 'Ele pode estar fora do ar.' } });

    expect(screen.getByTestId('sync-request-status')).toHaveTextContent(
      'Ele pode estar fora do ar.',
    );
    expect(screen.getByRole('button', { name: 'Sincronizar agora' })).toBeEnabled();
  });
});
