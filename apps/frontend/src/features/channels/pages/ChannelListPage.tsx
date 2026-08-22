import { Alert, Button, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { Link } from 'react-router';
import { useChannelsQuery } from '../api/useChannels';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { ChannelTable } from '../components/ChannelTable';

export function ChannelListPage() {
  const { data, isLoading, isError } = useChannelsQuery();
  const agentsQuery = useAgentsQuery();
  const loading = isLoading || agentsQuery.isLoading;

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>Canais</Title>
        <Button component={Link} to="/channels/new">
          Novo canal
        </Button>
      </Group>

      {loading && (
        <Group>
          <Loader size="sm" />
          <Text>Carregando canais...</Text>
        </Group>
      )}

      {isError && <Alert color="red">Não foi possível carregar os canais.</Alert>}

      {!loading && !isError && data?.length === 0 && (
        <Text c="dimmed">Nenhum canal cadastrado ainda.</Text>
      )}

      {!loading && !isError && data && data.length > 0 && (
        <ChannelTable channels={data} agents={agentsQuery.data ?? []} />
      )}
    </Stack>
  );
}
