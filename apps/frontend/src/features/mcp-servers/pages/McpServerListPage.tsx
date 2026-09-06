import { useState } from 'react';
import { Alert, Button, Group, Loader, Stack, Text, TextInput, Title } from '@mantine/core';
import { Link } from 'react-router';
import { useMcpServersQuery } from '../api/useMcpServers';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { McpServerTable } from '../components/McpServerTable';
import { matchesSearch } from '../../../utils/searchText';

export function McpServerListPage() {
  const { data, isLoading, isError } = useMcpServersQuery();
  // Só para derivar a coluna de uso. Uma falha aqui não impede a listagem:
  // a coluna fica vazia e o resto continua servindo (Decision 2 do
  // design.md da change frontend-mcp-servidor-uso-e-diagnostico).
  const agentsQuery = useAgentsQuery();
  const [search, setSearch] = useState('');

  const mcpServers = data ?? [];
  const visible = mcpServers.filter((mcpServer) =>
    matchesSearch(search, mcpServer.name, mcpServer.url),
  );

  const hasMcpServers = mcpServers.length > 0;
  const showEmptyCatalog = !isLoading && !isError && !hasMcpServers;
  const showNoMatches = !isLoading && !isError && hasMcpServers && visible.length === 0;

  return (
    <Stack>
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={2}>Servidores MCP</Title>
          {hasMcpServers && (
            <Text size="sm" c="dimmed">
              {mcpServers.length}{' '}
              {mcpServers.length === 1 ? 'servidor cadastrado' : 'servidores cadastrados'} · fontes
              de tools para os agentes
            </Text>
          )}
        </Stack>
        <Button component={Link} to="/mcp-servers/new">
          Novo servidor MCP
        </Button>
      </Group>

      {hasMcpServers && (
        <TextInput
          label="Buscar"
          placeholder="Buscar por nome ou url"
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          maw={340}
        />
      )}

      {isLoading && (
        <Group>
          <Loader size="sm" />
          <Text>Carregando servidores MCP...</Text>
        </Group>
      )}

      {isError && <Alert color="red">Não foi possível carregar os servidores MCP.</Alert>}

      {showEmptyCatalog && <Text c="dimmed">Nenhum servidor MCP cadastrado ainda.</Text>}

      {showNoMatches && <Text c="dimmed">Nenhum servidor corresponde à busca.</Text>}

      {visible.length > 0 && <McpServerTable mcpServers={visible} agents={agentsQuery.data} />}
    </Stack>
  );
}
