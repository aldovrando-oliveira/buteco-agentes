import { Alert, Button, Group, Loader, Modal, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { Link, useParams } from 'react-router';
import {
  useActivateAgentMutation,
  useAgentQuery,
  useDeactivateAgentMutation,
} from '../api/useAgents';
import { AgentDetailCard } from '../components/AgentDetailCard';
import { ApiError } from '../api/agentsApi';

export function AgentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data, isLoading, error } = useAgentQuery(id!);
  const activateMutation = useActivateAgentMutation();
  const deactivateMutation = useDeactivateAgentMutation();
  const [confirmOpened, { open: openConfirm, close: closeConfirm }] = useDisclosure(false);

  const handleActivate = () => {
    activateMutation.mutate(id!, {
      onSuccess: (agent) => {
        notifications.show({
          color: 'green',
          title: 'Agente ativado',
          message: `"${agent.name}" foi ativado com sucesso.`,
        });
      },
      onError: () => {
        notifications.show({
          color: 'red',
          title: 'Erro ao ativar agente',
          message: 'Não foi possível ativar o agente. Tente novamente.',
        });
      },
    });
  };

  const handleConfirmDeactivate = () => {
    deactivateMutation.mutate(id!, {
      onSuccess: (agent) => {
        closeConfirm();
        notifications.show({
          color: 'green',
          title: 'Agente desativado',
          message: `"${agent.name}" foi desativado com sucesso.`,
        });
      },
      onError: () => {
        closeConfirm();
        notifications.show({
          color: 'red',
          title: 'Erro ao desativar agente',
          message: 'Não foi possível desativar o agente. Tente novamente.',
        });
      },
    });
  };

  if (isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando agente...</Text>
      </Group>
    );
  }

  if (error instanceof ApiError && error.status === 404) {
    return <Alert color="red">Agente não encontrado.</Alert>;
  }

  if (error || !data) {
    return <Alert color="red">Não foi possível carregar o agente.</Alert>;
  }

  return (
    <>
      <AgentDetailCard agent={data} />
      <Group mt="md">
        <Button component={Link} to={`/agents/${data.id}/edit`} variant="default">
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

      <Modal opened={confirmOpened} onClose={closeConfirm} title="Confirmar desativação">
        <Text size="sm">
          Mensagens enviadas a este agente enquanto ele estiver inativo serão rejeitadas. Tem
          certeza que deseja desativar "{data.name}"?
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
