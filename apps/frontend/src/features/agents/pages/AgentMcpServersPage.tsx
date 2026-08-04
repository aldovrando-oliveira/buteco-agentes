import { useState } from 'react';
import { Alert, Button, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate, useParams } from 'react-router';
import { useAgentQuery, useReplaceAgentMcpServersMutation } from '../api/useAgents';
import { useMcpServersQuery } from '../../mcp-servers/api/useMcpServers';
import { ApiError } from '../api/agentsApi';
import { AgentMcpServerRow } from '../components/AgentMcpServerRow';
import type { Agent } from '../types/agent';
import type { McpServer } from '../../mcp-servers/types/mcpServer';

interface AgentMcpServersManagerProps {
  agent: Agent;
  mcpServers: McpServer[];
}

function AgentMcpServersManager({ agent, mcpServers }: AgentMcpServersManagerProps) {
  const navigate = useNavigate();
  const mutation = useReplaceAgentMcpServersMutation(agent.id);
  const [selection, setSelection] = useState<Record<string, string[]>>(() =>
    Object.fromEntries(agent.mcpServers.map((mcpServer) => [mcpServer.id, mcpServer.allowedTools])),
  );
  const [submitError, setSubmitError] = useState<{ title: string; detail?: string } | undefined>();

  const handleToggleSelected = (mcpServerId: string, selected: boolean) => {
    setSelection((prev) => {
      if (selected) {
        return { ...prev, [mcpServerId]: prev[mcpServerId] ?? [] };
      }
      return Object.fromEntries(
        Object.entries(prev).filter(([id]) => id !== mcpServerId),
      );
    });
  };

  const handleToggleTool = (mcpServerId: string, toolName: string, checked: boolean) => {
    setSelection((prev) => {
      if (!(mcpServerId in prev)) {
        return prev;
      }
      const current = prev[mcpServerId];
      const next = checked ? [...current, toolName] : current.filter((tool) => tool !== toolName);
      return { ...prev, [mcpServerId]: next };
    });
  };

  const handleToolsDiscovered = (mcpServerId: string, toolNames: string[]) => {
    setSelection((prev) => {
      if (!(mcpServerId in prev)) {
        return prev;
      }
      const filtered = prev[mcpServerId].filter((tool) => toolNames.includes(tool));
      if (filtered.length === prev[mcpServerId].length) {
        return prev;
      }
      return { ...prev, [mcpServerId]: filtered };
    });
  };

  const handleSubmit = () => {
    setSubmitError(undefined);
    const bindings = Object.entries(selection).map(([mcpServerId, allowedTools]) => ({
      mcpServerId,
      allowedTools,
    }));

    mutation.mutate(bindings, {
      onSuccess: () => {
        notifications.show({
          color: 'green',
          title: 'Vínculo atualizado',
          message: `Os servidores MCP de "${agent.name}" foram atualizados com sucesso.`,
        });
        navigate(`/agents/${agent.id}`);
      },
      onError: (error) => {
        if (error instanceof ApiError && error.status === 502) {
          setSubmitError({
            title: error.problem?.title ?? error.message,
            detail: error.problem?.detail,
          });
          return;
        }
        notifications.show({
          color: 'red',
          title: 'Erro ao atualizar vínculo',
          message: 'Não foi possível atualizar os servidores MCP do agente. Tente novamente.',
        });
      },
    });
  };

  return (
    <Stack>
      <Title order={2}>Servidores MCP de "{agent.name}"</Title>

      {submitError && (
        <Alert color="red" title={submitError.title}>
          {submitError.detail ?? 'Nenhum vínculo foi salvo. Corrija e tente novamente.'}
        </Alert>
      )}

      <Stack gap="sm">
        {mcpServers.map((mcpServer) => (
          <AgentMcpServerRow
            key={mcpServer.id}
            mcpServer={mcpServer}
            selected={mcpServer.id in selection}
            allowedTools={selection[mcpServer.id] ?? []}
            onToggleSelected={(selected) => handleToggleSelected(mcpServer.id, selected)}
            onToggleTool={(toolName, checked) => handleToggleTool(mcpServer.id, toolName, checked)}
            onToolsDiscovered={(toolNames) => handleToolsDiscovered(mcpServer.id, toolNames)}
          />
        ))}
      </Stack>

      <Group>
        <Button onClick={handleSubmit} loading={mutation.isPending}>
          Salvar vínculo
        </Button>
        <Button variant="default" onClick={() => navigate(`/agents/${agent.id}`)}>
          Cancelar
        </Button>
      </Group>
    </Stack>
  );
}

export function AgentMcpServersPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const agentQuery = useAgentQuery(id!);
  const mcpServersQuery = useMcpServersQuery();

  if (agentQuery.isLoading || mcpServersQuery.isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando...</Text>
      </Group>
    );
  }

  if (agentQuery.error instanceof ApiError && agentQuery.error.status === 404) {
    return <Alert color="red">Agente não encontrado.</Alert>;
  }

  if (agentQuery.error || !agentQuery.data) {
    return <Alert color="red">Não foi possível carregar o agente.</Alert>;
  }

  if (mcpServersQuery.isError) {
    return <Alert color="red">Não foi possível carregar os servidores MCP.</Alert>;
  }

  if (!mcpServersQuery.data || mcpServersQuery.data.length === 0) {
    return (
      <Stack>
        <Title order={2}>Servidores MCP de "{agentQuery.data.name}"</Title>
        <Text c="dimmed">Nenhum servidor MCP cadastrado ainda.</Text>
        <Group>
          <Button variant="default" onClick={() => navigate(`/agents/${id}`)}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    );
  }

  return <AgentMcpServersManager agent={agentQuery.data} mcpServers={mcpServersQuery.data} />;
}
