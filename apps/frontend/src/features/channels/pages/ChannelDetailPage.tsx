import {
  Alert,
  Badge,
  Button,
  Card,
  CopyButton,
  Grid,
  Group,
  Loader,
  Modal,
  ScrollArea,
  Stack,
  Tabs,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { Link, useNavigate, useParams } from 'react-router';
import {
  useActivateChannelMutation,
  useChannelQuery,
  useDeactivateChannelMutation,
} from '../api/useChannels';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { ApiError } from '../api/channelsApi';
import type { ChannelType } from '../types/channel';
import { useChannelSessionsQuery, useSessionMessagesQuery } from '../../sessions/api/useSessions';
import { SessionList } from '../../sessions/components/SessionList';
import { MessageTimeline } from '../../sessions/components/MessageTimeline';

const channelTypeLabels: Record<ChannelType, string> = {
  waha: 'WAHA',
  telegram: 'Telegram',
};

// Textos de instrução por ChannelType (design.md, Decision 3): WAHA exige
// configuração manual da sessão; Telegram já é provisionado automaticamente
// no cadastro (inbox-adapter-telegram).
const webhookInstructions: Record<ChannelType, string> = {
  waha: 'Configure manualmente a sessão do WAHA para usar esta URL como webhook.',
  telegram:
    'O webhook já foi configurado automaticamente junto ao Telegram no momento do cadastro — nenhuma ação manual é necessária.',
};

interface SessionsTabProps {
  channelId: string;
  sessionId?: string;
}

function SessionsTab({ channelId, sessionId }: SessionsTabProps) {
  const navigate = useNavigate();
  const sessionsQuery = useChannelSessionsQuery(channelId);
  const messagesQuery = useSessionMessagesQuery(sessionId);

  const handleSelect = (selectedSessionId: string) => {
    navigate(`/channels/${channelId}/sessions/${selectedSessionId}`);
  };

  return (
    <Grid mt="md">
      <Grid.Col span={4}>
        <ScrollArea h={500} type="auto">
          {sessionsQuery.isLoading && (
            <Group>
              <Loader size="sm" />
              <Text>Carregando sessões...</Text>
            </Group>
          )}
          {sessionsQuery.isError && (
            <Alert color="red">Não foi possível carregar as sessões.</Alert>
          )}
          {!sessionsQuery.isLoading && !sessionsQuery.isError && (
            <SessionList
              sessions={sessionsQuery.data ?? []}
              selectedSessionId={sessionId}
              onSelect={handleSelect}
            />
          )}
        </ScrollArea>
      </Grid.Col>
      <Grid.Col span={8}>
        <ScrollArea h={500} type="auto">
          {!sessionId && <Text c="dimmed">Selecione uma sessão para ver a conversa.</Text>}
          {sessionId && messagesQuery.isLoading && (
            <Group>
              <Loader size="sm" />
              <Text>Carregando mensagens...</Text>
            </Group>
          )}
          {sessionId && messagesQuery.isError && (
            <Alert color="red">Não foi possível carregar as mensagens desta sessão.</Alert>
          )}
          {sessionId && !messagesQuery.isLoading && !messagesQuery.isError && (
            <MessageTimeline messages={messagesQuery.data ?? []} />
          )}
        </ScrollArea>
      </Grid.Col>
    </Grid>
  );
}

export function ChannelDetailPage() {
  const { id, sessionId } = useParams<{ id: string; sessionId?: string }>();
  const { data, isLoading, error } = useChannelQuery(id!);
  const agentsQuery = useAgentsQuery();
  const activateMutation = useActivateChannelMutation();
  const deactivateMutation = useDeactivateChannelMutation();
  const [confirmOpened, { open: openConfirm, close: closeConfirm }] = useDisclosure(false);

  const handleActivate = () => {
    activateMutation.mutate(id!, {
      onSuccess: (channel) => {
        notifications.show({
          color: 'green',
          title: 'Canal ativado',
          message: `"${channel.name}" foi ativado com sucesso.`,
        });
      },
      onError: () => {
        notifications.show({
          color: 'red',
          title: 'Erro ao ativar canal',
          message: 'Não foi possível ativar o canal. Tente novamente.',
        });
      },
    });
  };

  const handleConfirmDeactivate = () => {
    deactivateMutation.mutate(id!, {
      onSuccess: (channel) => {
        closeConfirm();
        notifications.show({
          color: 'green',
          title: 'Canal desativado',
          message: `"${channel.name}" foi desativado com sucesso.`,
        });
      },
      onError: () => {
        closeConfirm();
        notifications.show({
          color: 'red',
          title: 'Erro ao desativar canal',
          message: 'Não foi possível desativar o canal. Tente novamente.',
        });
      },
    });
  };

  if (isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando canal...</Text>
      </Group>
    );
  }

  if (error instanceof ApiError && error.status === 404) {
    return <Alert color="red">Canal não encontrado.</Alert>;
  }

  if (error || !data) {
    return <Alert color="red">Não foi possível carregar o canal.</Alert>;
  }

  const responsibleAgent = agentsQuery.data?.find((agent) => agent.id === data.agentId);
  const agentLabel = responsibleAgent
    ? responsibleAgent.isActive
      ? responsibleAgent.name
      : `${responsibleAgent.name} (inativo)`
    : data.agentId;

  return (
    <>
      <Group mb="md">
        <Button component={Link} to={`/channels/${data.id}/edit`} variant="default">
          Editar
        </Button>
        {data.isActive ? (
          <Button color="red" variant="outline" onClick={openConfirm}>
            Desativar
          </Button>
        ) : (
          <Button
            color="green"
            variant="outline"
            onClick={handleActivate}
            loading={activateMutation.isPending}
          >
            Ativar
          </Button>
        )}
      </Group>

      <Tabs defaultValue="sessions">
        <Tabs.List>
          <Tabs.Tab value="sessions">Sessões</Tabs.Tab>
          <Tabs.Tab value="config">Configuração</Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="sessions">
          <SessionsTab channelId={data.id} sessionId={sessionId} />
        </Tabs.Panel>

        <Tabs.Panel value="config">
          <Card withBorder mt="md">
            <Stack gap="sm">
              <Group justify="space-between">
                <Title order={2}>{data.name}</Title>
                <Badge color={data.isActive ? 'green' : 'gray'}>
                  {data.isActive ? 'Ativo' : 'Inativo'}
                </Badge>
              </Group>
              <Text size="sm" c="dimmed">
                Tipo: {channelTypeLabels[data.channelType]} · Agente responsável: {agentLabel}
              </Text>

              <Group align="flex-end" gap="xs">
                <TextInput
                  label="URL de webhook"
                  value={data.webhookUrl}
                  readOnly
                  style={{ flex: 1 }}
                />
                <CopyButton value={data.webhookUrl}>
                  {({ copied, copy }) => (
                    <Button variant="default" onClick={copy}>
                      {copied ? 'Copiado' : 'Copiar'}
                    </Button>
                  )}
                </CopyButton>
              </Group>
              <Text size="sm" c="dimmed">
                {webhookInstructions[data.channelType]}
              </Text>

              <Text size="sm" c="dimmed">
                Criado em {new Date(data.createdAt).toLocaleString('pt-BR')}
              </Text>
              <Text size="sm" c="dimmed">
                Atualizado em {new Date(data.updatedAt).toLocaleString('pt-BR')}
              </Text>
            </Stack>
          </Card>
        </Tabs.Panel>
      </Tabs>

      <Modal opened={confirmOpened} onClose={closeConfirm} title="Confirmar desativação">
        <Text size="sm">
          O canal deixará de receber mensagens enquanto estiver inativo. Tem certeza que deseja
          desativar "{data.name}"?
        </Text>
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={closeConfirm}>
            Cancelar
          </Button>
          <Button
            color="red"
            onClick={handleConfirmDeactivate}
            loading={deactivateMutation.isPending}
          >
            Confirmar desativação
          </Button>
        </Group>
      </Modal>
    </>
  );
}
