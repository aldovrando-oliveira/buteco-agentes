import { useState } from 'react';
import { Alert, Badge, Button, Grid, Group, Loader, Modal, Stack, Text } from '@mantine/core';
import { DetailHeader } from '../../../components/layout/DetailHeader';
import { useDisclosure } from '@mantine/hooks';
import { notifications } from '@mantine/notifications';
import { Link, useParams } from 'react-router';
import {
  useActivateMcpServerMutation,
  useDeactivateMcpServerMutation,
  useMcpServerQuery,
  useTestSavedMcpServerConnectionMutation,
} from '../api/useMcpServers';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { McpServerAgentsCard } from '../components/McpServerAgentsCard';
import { McpServerConfigCard } from '../components/McpServerConfigCard';
import { McpServerToolsCatalog } from '../components/McpServerToolsCatalog';
import {
  ConnectionTestResultAlert,
  type ConnectionTestResult,
} from '../components/ConnectionTestResultAlert';
import { agentsUsingServer } from '../utils/agentUsage';
import { ApiError } from '../api/mcpServersApi';

export function McpServerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data, isLoading, error } = useMcpServerQuery(id!);
  // Só para derivar o uso: quem usa o servidor, quais tools estão
  // permitidas e quem é afetado pela desativação. Uma falha aqui não
  // impede o detalhe de carregar (Decision 2 do design.md).
  const agentsQuery = useAgentsQuery();
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
        setTestResult({
          success: result.success,
          failureReason: result.failureReason,
          message: result.message,
          testedAt: new Date(),
        });
      },
      onError: () => {
        setTestResult({
          success: false,
          failureReason: null,
          message: 'Não foi possível testar a conexão. Tente novamente.',
          testedAt: new Date(),
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

  const affectedAgents = agentsQuery.data ? agentsUsingServer(agentsQuery.data, data.id) : [];

  return (
    <Stack gap="md">
      <DetailHeader
        backTo="/mcp-servers"
        backLabel="Servidores MCP"
        title={data.name}
        status={
          <Badge color={data.isActive ? 'green' : 'gray'}>
            {data.isActive ? 'Ativo' : 'Inativo'}
          </Badge>
        }
        description={data.description ?? undefined}
        actions={
          <>
            <Button component={Link} to={`/mcp-servers/${data.id}/edit`} variant="default">
              Editar
            </Button>
            <Button
              variant="outline"
              onClick={handleTestConnection}
              loading={testMutation.isPending}
            >
              Testar conexão
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
          </>
        }
      />

      <ConnectionTestResultAlert result={testResult} />

      <Grid gap="md" align="start">
        <Grid.Col span={{ base: 12, md: 5 }}>
          <McpServerConfigCard mcpServer={data} />
        </Grid.Col>
        <Grid.Col span={{ base: 12, md: 7 }}>
          <McpServerToolsCatalog mcpServerId={data.id} agents={agentsQuery.data} />
        </Grid.Col>
      </Grid>

      <McpServerAgentsCard mcpServerId={data.id} agents={agentsQuery.data} />

      <Modal opened={confirmOpened} onClose={closeConfirm} title="Confirmar desativação">
        <Stack gap="sm">
          <Text size="sm">
            Agentes vinculados a este servidor MCP deixarão de conseguir usar suas tools enquanto
            ele estiver inativo. Tem certeza que deseja desativar "{data.name}"?
          </Text>

          {affectedAgents.length > 0 ? (
            <Alert color="yellow" data-testid="deactivate-affected-agents">
              Afeta {affectedAgents.length} {affectedAgents.length === 1 ? 'agente' : 'agentes'}:{' '}
              {affectedAgents.map((usage) => usage.agent.name).join(', ')}
            </Alert>
          ) : (
            <Text size="sm" c="dimmed" data-testid="deactivate-no-agents">
              Nenhum agente usa este servidor no momento.
            </Text>
          )}
        </Stack>

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
    </Stack>
  );
}
