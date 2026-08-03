import { useState } from 'react';
import { Alert, Button, Group, Loader, Modal, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { Link, useParams } from 'react-router';
import {
  useActivateMcpServerMutation,
  useDeactivateMcpServerMutation,
  useMcpServerQuery,
  useTestSavedMcpServerConnectionMutation,
} from '../api/useMcpServers';
import { McpServerDetailCard } from '../components/McpServerDetailCard';
import {
  ConnectionTestResultAlert,
  type ConnectionTestResult,
} from '../components/ConnectionTestResultAlert';
import { ApiError } from '../api/mcpServersApi';

export function McpServerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data, isLoading, error } = useMcpServerQuery(id!);
  const activateMutation = useActivateMcpServerMutation();
  const deactivateMutation = useDeactivateMcpServerMutation();
  const testMutation = useTestSavedMcpServerConnectionMutation();
  const [testResult, setTestResult] = useState<ConnectionTestResult | undefined>();
  const [confirmOpened, { open: openConfirm, close: closeConfirm }] = useDisclosure(false);

  const handleActivate = () => {
    activateMutation.mutate(id!, {
      onSuccess: (mcpServer) => {
        notifications.show({
          color: 'green',
          title: 'Servidor MCP ativado',
          message: `"${mcpServer.name}" foi ativado com sucesso.`,
        });
      },
      onError: () => {
        notifications.show({
          color: 'red',
          title: 'Erro ao ativar servidor MCP',
          message: 'Não foi possível ativar o servidor MCP. Tente novamente.',
        });
      },
    });
  };

  const handleConfirmDeactivate = () => {
    deactivateMutation.mutate(id!, {
      onSuccess: (mcpServer) => {
        closeConfirm();
        notifications.show({
          color: 'green',
          title: 'Servidor MCP desativado',
          message: `"${mcpServer.name}" foi desativado com sucesso.`,
        });
      },
      onError: () => {
        closeConfirm();
        notifications.show({
          color: 'red',
          title: 'Erro ao desativar servidor MCP',
          message: 'Não foi possível desativar o servidor MCP. Tente novamente.',
        });
      },
    });
  };

  const handleTestConnection = () => {
    testMutation.mutate(id!, {
      onSuccess: (result) => {
        setTestResult({ success: result.success, message: result.message });
      },
      onError: () => {
        setTestResult({
          success: false,
          message: 'Não foi possível testar a conexão. Tente novamente.',
        });
      },
    });
  };

  if (isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando servidor MCP...</Text>
      </Group>
    );
  }

  if (error instanceof ApiError && error.status === 404) {
    return <Alert color="red">Servidor MCP não encontrado.</Alert>;
  }

  if (error || !data) {
    return <Alert color="red">Não foi possível carregar o servidor MCP.</Alert>;
  }

  return (
    <>
      <Group mb="md">
        <Button component={Link} to={`/mcp-servers/${data.id}/edit`} variant="default">
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
        <Button variant="outline" onClick={handleTestConnection} loading={testMutation.isPending}>
          Testar conexão
        </Button>
      </Group>
      <ConnectionTestResultAlert result={testResult} />
      <McpServerDetailCard mcpServer={data} />

      <Modal opened={confirmOpened} onClose={closeConfirm} title="Confirmar desativação">
        <Text size="sm">
          Agentes vinculados a este servidor MCP deixarão de conseguir usar suas tools enquanto ele
          estiver inativo. Tem certeza que deseja desativar "{data.name}"?
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
