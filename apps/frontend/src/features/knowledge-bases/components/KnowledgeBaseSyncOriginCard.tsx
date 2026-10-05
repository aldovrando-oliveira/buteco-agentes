import { Alert, Anchor, Button, Group, Loader, SimpleGrid, Stack, Text } from '@mantine/core';
import { Check, CircleAlert, Folder } from 'lucide-react';
import type { ReactNode } from 'react';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { providerLabel } from '../utils/connectorErrors';
import { formatSyncInstant, syncStatus } from '../utils/syncState';
import { syncFailureMessage } from '../utils/syncStateMessages';
import type { KnowledgeBaseSyncSource, KnowledgeBaseSyncState } from '../types/knowledgeBase';

// O que a página sabe do pedido de "Sincronizar agora" (design.md da change
// frontend-detalhe-base-sincronizada, D5):
//   idle      — nenhum pedido nesta visita;
//   starting  — relendo a base e chamando o apps/connectors;
//   waiting   — 202 recebido, `lastFinishedAt` ainda igual à linha de base;
//   finished  — `lastFinishedAt` mudou: o resultado é o `syncState` recebido;
//   timed-out — 5 minutos sem mudança;
//   error     — o pedido falhou, com o texto da tabela da D4.
export type SyncRequestView =
  | { phase: 'idle' | 'starting' | 'waiting' | 'finished' | 'timed-out' }
  | { phase: 'error'; message: string };

interface KnowledgeBaseSyncOriginCardProps {
  syncSource: KnowledgeBaseSyncSource;
  syncState: KnowledgeBaseSyncState;
  // `false` sem VITE_CONNECTORS_BASE_URL no build: o botão fica indisponível com
  // explicação, e o resto do card continua, porque vem do apps/api (D5).
  syncAvailable: boolean;
  request: SyncRequestView;
  onSync: () => void;
}

const FOLDER_LABEL = {
  'never-synced': 'Pasta',
  'up-to-date': 'Pasta',
  failing: 'Pasta — nome na última sincronização concluída',
  // Nenhum sucesso trocou o nome: é o da validação da pasta no cadastro (#104).
  // Chamá-lo de "nome na última sincronização concluída" afirmaria uma
  // sincronização que não existiu (D2, correção de protótipo C11).
  'failing-never-completed': 'Pasta — nome no cadastro',
} as const;

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Stack gap={4}>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      {children}
    </Stack>
  );
}

// Card de origem de base sincronizada (prancha 4a/4b), apresentacional: recebe o
// estado e o pedido, e devolve o clique (convenção 7).
export function KnowledgeBaseSyncOriginCard({
  syncSource,
  syncState,
  syncAvailable,
  request,
  onSync,
}: KnowledgeBaseSyncOriginCardProps) {
  const status = syncStatus(syncState);
  const failing = status === 'failing' || status === 'failing-never-completed';
  const knownProvider = providerLabel(syncSource.provider) !== syncSource.provider;

  const failure =
    failing && syncState.lastError
      ? syncFailureMessage(syncState.lastError, {
          completedBefore: status === 'failing',
          providerKey: syncSource.provider,
        })
      : null;

  const syncButton = (
    <Button
      size="xs"
      variant="default"
      onClick={onSync}
      disabled={!syncAvailable || request.phase === 'waiting'}
      loading={request.phase === 'starting'}
    >
      {failing ? 'Tentar sincronizar agora' : 'Sincronizar agora'}
    </Button>
  );

  return (
    <SectionedCard
      title="Origem — pasta sincronizada"
      action={syncButton}
      data-testid="sync-origin-card"
    >
      <SectionedCard.Body>
        <Stack gap="md">
          <SimpleGrid cols={3} spacing={20}>
            <Field label={FOLDER_LABEL[status]}>
              <Group gap={8} wrap="nowrap">
                <Folder size={18} aria-hidden color="var(--mantine-color-dimmed)" />
                <Text fw={600}>{syncSource.folderName}</Text>
              </Group>
              <Anchor href={syncSource.folderUrl} target="_blank" rel="noreferrer" size="sm">
                {knownProvider
                  ? `Abrir no ${providerLabel(syncSource.provider)} ↗`
                  : 'Abrir a pasta ↗'}
              </Anchor>
            </Field>

            <Field label="Provedor">
              <Text>{providerLabel(syncSource.provider)}</Text>
            </Field>

            {/* `lastCompletedAt`, e nunca `lastFinishedAt`: em falha, este é o
                instante de um ciclo que falhou (D2). */}
            <Field label="Última sincronização concluída">
              <Text>
                {syncState.lastCompletedAt
                  ? formatSyncInstant(syncState.lastCompletedAt)
                  : 'Nenhuma ainda'}
              </Text>
              {status === 'never-synced' && (
                // Espera, não falha: tom neutro, sem alerta. "A cada 5 minutos" é
                // o intervalo da rodada (SyncSchedulerService.cs:13); a frase não
                // promete prazo, porque a próxima rodada só começa quando a
                // anterior termina. Gatilho: o intervalo mudar.
                <Text size="xs" c="dimmed">
                  A base entra na próxima rodada de sincronização, que roda a cada 5 minutos.
                </Text>
              )}
              {status === 'up-to-date' && (
                <Group gap={4} wrap="nowrap">
                  <Check size={14} aria-hidden color="var(--buteco-ok-text)" />
                  <Text size="xs" c="var(--buteco-ok-text)">
                    Sem erros no último ciclo
                  </Text>
                </Group>
              )}
              {failing && syncState.failingSince && (
                <Group gap={4} wrap="nowrap">
                  <CircleAlert size={14} aria-hidden color="var(--mantine-color-red-text)" />
                  <Text size="xs" fw={600} c="var(--mantine-color-red-text)">
                    {`Falhando desde ${formatSyncInstant(syncState.failingSince)}`}
                  </Text>
                </Group>
              )}
            </Field>
          </SimpleGrid>

          {/* `Alert` com a variante default `light`, que troca sozinha de esquema
              (convenção 16, C7): o papel é o mesmo da faixa de falha de
              documento. */}
          {failure && (
            <Alert color="red" title={failure.title} data-testid="sync-failure-alert">
              <Stack gap={6}>
                <Text size="sm">{failure.sentence}</Text>
                <Text size="sm">{failure.guarantee}</Text>
                {failure.remedy && (
                  <Text size="sm" c="dimmed">
                    {failure.remedy}
                  </Text>
                )}
              </Stack>
            </Alert>
          )}

          {!syncAvailable && (
            <Text size="xs" c="dimmed" data-testid="sync-unavailable">
              A sincronização manual não está habilitada neste painel: falta o endereço do serviço
              de conectores na construção do painel.
            </Text>
          )}

          <SyncRequestStatus request={request} syncState={syncState} failing={failing} />
        </Stack>
      </SectionedCard.Body>
    </SectionedCard>
  );
}

// O resultado do pedido NUNCA aparece antes de `lastFinishedAt` mudar: em
// `waiting` a tela diz só que pediu (aceite da #107).
function SyncRequestStatus({
  request,
  syncState,
  failing,
}: {
  request: SyncRequestView;
  syncState: KnowledgeBaseSyncState;
  failing: boolean;
}) {
  switch (request.phase) {
    case 'waiting':
      return (
        <Group gap="xs" data-testid="sync-request-status">
          <Loader size={14} />
          <Text size="sm">Sincronização solicitada. Aguardando o resultado…</Text>
        </Group>
      );
    case 'finished':
      return failing || !syncState.lastCompletedAt ? (
        <Text
          size="sm"
          fw={600}
          c="var(--mantine-color-red-text)"
          data-testid="sync-request-status"
        >
          A sincronização terminou com falha.
        </Text>
      ) : (
        <Text size="sm" c="var(--buteco-ok-text)" data-testid="sync-request-status">
          {`Sincronização concluída em ${formatSyncInstant(syncState.lastCompletedAt)}.`}
        </Text>
      );
    // Sem afirmar sucesso nem falha: há caminhos do ciclo que não gravam nada
    // (base com 404, apps/api fora, exceção), e o estado mostrado é o último
    // gravado (D5).
    case 'timed-out':
      return (
        <Alert color="yellow" data-testid="sync-request-status">
          Nenhum resultado foi gravado em 5 minutos. A sincronização pode continuar rodando, ou ter
          terminado sem gravar resultado; o estado acima é o último gravado.
        </Alert>
      );
    case 'error':
      return (
        <Alert color="red" data-testid="sync-request-status">
          {request.message}
        </Alert>
      );
    default:
      return null;
  }
}
