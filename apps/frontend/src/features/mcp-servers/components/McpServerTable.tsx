import { Anchor, Badge, Paper, Stack, Table, Text } from '@mantine/core';
import { Link } from 'react-router';
import { serverUsageSummary } from '../utils/agentUsage';
import type { McpServer } from '../types/mcpServer';
import type { Agent } from '../../agents/types/agent';

interface McpServerTableProps {
  mcpServers: McpServer[];
  // Catálogo de agentes usado só para derivar o uso de cada servidor. Vem
  // por propriedade, buscado pela página, e pode ser indefinido quando a
  // consulta falha — nesse caso a coluna de uso fica de fora e o resto da
  // tabela continua servindo (Decision 2 do design.md da change
  // frontend-mcp-servidor-uso-e-diagnostico).
  agents?: Agent[];
}

const authTypeLabels: Record<McpServer['authType'], string> = {
  None: 'Nenhuma',
  BearerToken: 'Bearer Token',
};

function UsageCell({ mcpServer, agents }: { mcpServer: McpServer; agents?: Agent[] }) {
  if (!agents) {
    return (
      <Text size="sm" c="dimmed">
        —
      </Text>
    );
  }

  const { agentCount, agentsWithoutToolsCount } = serverUsageSummary(agents, mcpServer.id);

  return (
    <Stack gap={2}>
      <Text size="sm">
        {agentCount === 0
          ? 'Nenhum agente'
          : `${agentCount} ${agentCount === 1 ? 'agente' : 'agentes'}`}
      </Text>
      {agentsWithoutToolsCount > 0 && (
        <Text size="xs" c="yellow" data-testid={`usage-without-tools-${mcpServer.id}`}>
          {agentsWithoutToolsCount} sem tools
        </Text>
      )}
    </Stack>
  );
}

// Superfície própria: o fundo da página deixou de ser branco (change
// frontend-tema-identidade-visual, D4/D12), então o conteúdo precisa declarar
// a sua em vez de herdar o branco do body.
export function McpServerTable({ mcpServers, agents }: McpServerTableProps) {
  return (
    <Paper withBorder radius="md" style={{ overflow: 'hidden' }}>
      <Table>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Nome</Table.Th>
            <Table.Th>Url</Table.Th>
            <Table.Th>Autenticação</Table.Th>
            <Table.Th>Usado por</Table.Th>
            <Table.Th>Estado</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {mcpServers.map((mcpServer) => (
            <Table.Tr key={mcpServer.id}>
              <Table.Td>
                <Anchor component={Link} to={`/mcp-servers/${mcpServer.id}`}>
                  {mcpServer.name}
                </Anchor>
              </Table.Td>
              <Table.Td>{mcpServer.url}</Table.Td>
              <Table.Td>{authTypeLabels[mcpServer.authType]}</Table.Td>
              <Table.Td>
                <UsageCell mcpServer={mcpServer} agents={agents} />
              </Table.Td>
              <Table.Td>
                <Badge color={mcpServer.isActive ? 'green' : 'gray'}>
                  {mcpServer.isActive ? 'Ativo' : 'Inativo'}
                </Badge>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Paper>
  );
}
