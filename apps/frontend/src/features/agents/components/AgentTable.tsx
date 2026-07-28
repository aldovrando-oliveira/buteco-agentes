import { Anchor, Table } from '@mantine/core';
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
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {agents.map((agent) => (
          <Table.Tr key={agent.id}>
            <Table.Td>
              <Anchor component={Link} to={`/agents/${agent.id}`}>
                {agent.name}
              </Anchor>
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}
