import { Alert, Button, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { Link } from 'react-router';
import { useAgentsQuery } from '../api/useAgents';
import { AgentTable } from '../components/AgentTable';

export function AgentListPage() {
  const { data, isLoading, isError } = useAgentsQuery();

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>Agentes</Title>
        <Button component={Link} to="/agents/new">
          Novo agente
        </Button>
      </Group>

      {isLoading && (
        <Group>
          <Loader size="sm" />
          <Text>Carregando agentes...</Text>
        </Group>
      )}

      {isError && <Alert color="red">Não foi possível carregar os agentes.</Alert>}

      {!isLoading && !isError && data?.length === 0 && (
        <Text c="dimmed">Nenhum agente cadastrado ainda.</Text>
      )}

      {!isLoading && !isError && data && data.length > 0 && <AgentTable agents={data} />}
    </Stack>
  );
}
