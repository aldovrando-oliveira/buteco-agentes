import { Anchor, Badge, Group, Paper, Stack, Table, Text } from '@mantine/core';
import { Link } from 'react-router';
import type { Agent } from '../types/agent';

interface AgentTableProps {
  agents: Agent[];
}

// Resumo do vínculo com servidores MCP daquele agente. Sai de
// `agent.mcpServers`, que a listagem já recebe — soma simples com um único
// consumidor, por isso vive aqui e não em módulo próprio (Decision 4 do
// design.md da change frontend-listas-busca-e-colunas).
function toolsSummary(agent: Agent) {
  const serverCount = agent.mcpServers.length;
  const toolCount = agent.mcpServers.reduce(
    (total, mcpServer) => total + mcpServer.allowedTools.length,
    0,
  );
  const serversWithoutTools = agent.mcpServers.filter(
    (mcpServer) => mcpServer.allowedTools.length === 0,
  ).length;

  return { serverCount, toolCount, serversWithoutTools };
}

function ToolsCell({ agent }: { agent: Agent }) {
  const { serverCount, toolCount, serversWithoutTools } = toolsSummary(agent);

  if (serverCount === 0) {
    return (
      <Text size="sm" c="dimmed">
        Nenhum servidor
      </Text>
    );
  }

  return (
    <Stack gap={2}>
      <Text size="sm">
        {serverCount} {serverCount === 1 ? 'servidor' : 'servidores'} · {toolCount}{' '}
        {toolCount === 1 ? 'tool' : 'tools'}
      </Text>
      {serversWithoutTools > 0 && (
        <Text size="xs" c="yellow" data-testid={`agent-servers-without-tools-${agent.id}`}>
          {serversWithoutTools} {serversWithoutTools === 1 ? 'servidor' : 'servidores'} sem tools
        </Text>
      )}
    </Stack>
  );
}

// Superfície própria: o fundo da página deixou de ser branco (change
// frontend-tema-identidade-visual, D4/D12), então o conteúdo precisa declarar
// a sua em vez de herdar o branco do body.
export function AgentTable({ agents }: AgentTableProps) {
  return (
    <Paper withBorder radius="md" style={{ overflow: 'hidden' }}>
      <Table>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Agente</Table.Th>
            <Table.Th>Provedor / modelo</Table.Th>
            <Table.Th>Ferramentas</Table.Th>
            <Table.Th>Delega para</Table.Th>
            <Table.Th>Estado</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {agents.map((agent) => {
            const needsReconfiguration = agent.provider === null || agent.model === null;

            return (
              <Table.Tr key={agent.id}>
                <Table.Td>
                  <Stack gap={2} style={{ minWidth: 0 }}>
                    <Anchor component={Link} to={`/agents/${agent.id}`}>
                      {agent.name}
                    </Anchor>
                    {agent.description && (
                      <Text size="xs" c="dimmed" truncate>
                        {agent.description}
                      </Text>
                    )}
                  </Stack>
                </Table.Td>

                <Table.Td>
                  <Text size="sm" ff="monospace" c="dimmed">
                    {agent.provider ?? '—'} / {agent.model ?? 'não configurado'}
                  </Text>
                </Table.Td>

                <Table.Td>
                  <ToolsCell agent={agent} />
                </Table.Td>

                <Table.Td>
                  {agent.delegatesTo.length === 0 ? (
                    <Text size="sm" c="dimmed">
                      —
                    </Text>
                  ) : (
                    <Text size="sm">
                      {agent.delegatesTo.map((delegate) => delegate.name).join(', ')}
                    </Text>
                  )}
                </Table.Td>

                <Table.Td>
                  <Group gap="xs">
                    {needsReconfiguration && (
                      <Badge color="yellow">Precisa de reconfiguração</Badge>
                    )}
                    <Badge color={agent.isActive ? 'green' : 'gray'}>
                      {agent.isActive ? 'Ativo' : 'Inativo'}
                    </Badge>
                  </Group>
                </Table.Td>
              </Table.Tr>
            );
          })}
        </Table.Tbody>
      </Table>
    </Paper>
  );
}
