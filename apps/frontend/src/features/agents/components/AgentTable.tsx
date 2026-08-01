import { Anchor, Badge, Group, Table } from '@mantine/core';
import { Link } from 'react-router';
import type { Agent } from '../types/agent';

interface AgentTableProps {
  agents: Agent[];
}

export function AgentTable({ agents }: AgentTableProps) {
  return (
    <Table>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Nome</Table.Th>
          <Table.Th>Provider</Table.Th>
          <Table.Th>Model</Table.Th>
          <Table.Th>Estado</Table.Th>
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {agents.map((agent) => {
          const needsReconfiguration = agent.provider === null || agent.model === null;

          return (
            <Table.Tr key={agent.id}>
              <Table.Td>
                <Anchor component={Link} to={`/agents/${agent.id}`}>
                  {agent.name}
                </Anchor>
              </Table.Td>
              <Table.Td>{agent.provider ?? '—'}</Table.Td>
              <Table.Td>{agent.model ?? '—'}</Table.Td>
              <Table.Td>
                <Group gap="xs">
                  {needsReconfiguration && <Badge color="yellow">Precisa de reconfiguração</Badge>}
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
  );
}
