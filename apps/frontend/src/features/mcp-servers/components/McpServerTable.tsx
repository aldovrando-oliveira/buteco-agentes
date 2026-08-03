import { Anchor, Badge, Table } from '@mantine/core';
import { Link } from 'react-router';
import type { McpServer } from '../types/mcpServer';

interface McpServerTableProps {
  mcpServers: McpServer[];
}

const authTypeLabels: Record<McpServer['authType'], string> = {
  None: 'Nenhuma',
  BearerToken: 'Bearer Token',
};

export function McpServerTable({ mcpServers }: McpServerTableProps) {
  return (
    <Table>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Nome</Table.Th>
          <Table.Th>Url</Table.Th>
          <Table.Th>Autenticação</Table.Th>
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
              <Badge color={mcpServer.isActive ? 'green' : 'gray'}>
                {mcpServer.isActive ? 'Ativo' : 'Inativo'}
              </Badge>
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}
