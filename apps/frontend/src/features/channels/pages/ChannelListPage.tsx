import { useState } from 'react';
import { Alert, Button, Group, Loader, Stack, Text, TextInput, Title } from '@mantine/core';
import { Link } from 'react-router';
import { useChannelsQuery } from '../api/useChannels';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { ChannelTable } from '../components/ChannelTable';
import { matchesSearch } from '../../../utils/searchText';

const channelTypeLabels: Record<string, string> = { waha: 'WAHA', telegram: 'Telegram' };

export function ChannelListPage() {
  const { data, isLoading, isError } = useChannelsQuery();
  const agentsQuery = useAgentsQuery();
  const loading = isLoading || agentsQuery.isLoading;
  const [search, setSearch] = useState('');

  const channels = data ?? [];
  const agents = agentsQuery.data ?? [];
  const hasChannels = channels.length > 0;

  // Busca no cliente sobre a coleção já carregada, como nas outras duas
  // listagens: a rota de canais também não tem busca nem paginação. Casa por
  // nome, tipo e nome do agente responsável — os três dados que a linha exibe.
  const visible = channels.filter((channel) =>
    matchesSearch(
      search,
      channel.name,
      channelTypeLabels[channel.channelType] ?? channel.channelType,
      agents.find((agent) => agent.id === channel.agentId)?.name,
    ),
  );

  return (
    <Stack>
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={2}>Canais</Title>
          {hasChannels && (
            <Text size="sm" c="dimmed">
              {channels.length} {channels.length === 1 ? 'canal cadastrado' : 'canais cadastrados'}{' '}
              · por onde as mensagens chegam aos agentes
            </Text>
          )}
        </Stack>
        <Button component={Link} to="/channels/new">
          Novo canal
        </Button>
      </Group>

      {hasChannels && (
        <TextInput
          aria-label="Buscar por nome, tipo ou agente"
          placeholder="Buscar por nome, tipo ou agente"
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          maw={340}
        />
      )}

      {loading && (
        <Group>
          <Loader size="sm" />
          <Text>Carregando canais...</Text>
        </Group>
      )}

      {isError && <Alert color="red">Não foi possível carregar os canais.</Alert>}

      {!loading && !isError && !hasChannels && (
        <Text c="dimmed">Nenhum canal cadastrado ainda.</Text>
      )}

      {/* Lista vazia por falta de cadastro e lista vazia por busca sem
          resultado são problemas diferentes, com saídas diferentes. */}
      {!loading && !isError && hasChannels && visible.length === 0 && (
        <Text c="dimmed">Nenhum canal corresponde à busca.</Text>
      )}

      {!loading && !isError && visible.length > 0 && (
        <ChannelTable channels={visible} agents={agents} />
      )}
    </Stack>
  );
}
