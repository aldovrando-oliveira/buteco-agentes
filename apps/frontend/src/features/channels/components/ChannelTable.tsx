import { Anchor, Badge, Table } from '@mantine/core';
import { Link } from 'react-router';
import type { Agent } from '../../agents/types/agent';
import type { Channel, ChannelType } from '../types/channel';

interface ChannelTableProps {
  channels: Channel[];
  agents: Agent[];
}

const channelTypeLabels: Record<ChannelType, string> = {
  waha: 'WAHA',
  telegram: 'Telegram',
};

function agentLabelFor(agents: Agent[], agentId: string): string {
  const agent = agents.find((candidate) => candidate.id === agentId);
  if (!agent) {
    return agentId;
  }
  return agent.isActive ? agent.name : `${agent.name} (inativo)`;
}

export function ChannelTable({ channels, agents }: ChannelTableProps) {
  return (
    <Table>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Nome</Table.Th>
          <Table.Th>Tipo</Table.Th>
          <Table.Th>Agente responsável</Table.Th>
          <Table.Th>Estado</Table.Th>
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {channels.map((channel) => (
          <Table.Tr key={channel.id}>
            <Table.Td>
              <Anchor component={Link} to={`/channels/${channel.id}`}>
                {channel.name}
              </Anchor>
            </Table.Td>
            <Table.Td>{channelTypeLabels[channel.channelType]}</Table.Td>
            <Table.Td>{agentLabelFor(agents, channel.agentId)}</Table.Td>
            <Table.Td>
              <Badge color={channel.isActive ? 'green' : 'gray'}>
                {channel.isActive ? 'Ativo' : 'Inativo'}
              </Badge>
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}
