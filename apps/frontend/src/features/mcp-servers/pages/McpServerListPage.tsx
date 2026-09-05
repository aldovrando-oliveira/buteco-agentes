import { Alert, Button, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { Link } from 'react-router';
import { useMcpServersQuery } from '../api/useMcpServers';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { McpServerTable } from '../components/McpServerTable';

export function McpServerListPage() {
  const { data, isLoading, isError } = useMcpServersQuery();
  // Só para derivar a coluna de uso. Uma falha aqui não impede a listagem:
  // a coluna fica vazia e o resto continua servindo (Decision 2 do
  // design.md da change frontend-mcp-servidor-uso-e-diagnostico).
  const agentsQuery = useAgentsQuery();

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>Servidores MCP</Title>
        <Button component={Link} to="/mcp-servers/new">
          Novo servidor MCP
        </Button>
      </Group>

      {isLoading && (
        <Group>
          <Loader size="sm" />
          <Text>Carregando servidores MCP...</Text>
        </Group>
      )}

      {isError && <Alert color="red">Não foi possível carregar os servidores MCP.</Alert>}

      {!isLoading && !isError && data?.length === 0 && (
        <Text c="dimmed">Nenhum servidor MCP cadastrado ainda.</Text>
      )}

      {!isLoading && !isError && data && data.length > 0 && <McpServerTable mcpServers={data} agents={agentsQuery.data} />}
    </Stack>
  );
}
