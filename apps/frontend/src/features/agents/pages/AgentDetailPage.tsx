import { Alert, Group, Loader, Text } from '@mantine/core';
import { useParams } from 'react-router';
import { useAgentQuery } from '../api/useAgents';
import { AgentDetailCard } from '../components/AgentDetailCard';
import { ApiError } from '../api/agentsApi';

export function AgentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data, isLoading, error } = useAgentQuery(id!);

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

  return <AgentDetailCard agent={data} />;
}
