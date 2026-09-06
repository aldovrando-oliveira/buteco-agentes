import { Anchor, Badge, Table } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { SectionLabel } from '../../../components/data/SectionLabel';
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

// Faixa no cabeçalho e rótulos de coluna em maiúsculas com espaçamento entre
// letras. O divisor entre as linhas já vinha de graça: o Mantine liga
// withRowBorders por padrão e usa a mesma cor de borda da identidade visual
// (design.md da change frontend-acabamento-telas, D1).
export function ChannelTable({ channels, agents }: ChannelTableProps) {
  return (
    <SectionedCard>
      <Table>
        <Table.Thead bg="var(--buteco-surface-subtle)">
          <Table.Tr>
            <Table.Th>
              <SectionLabel>Nome</SectionLabel>
            </Table.Th>
            <Table.Th>
              <SectionLabel>Tipo</SectionLabel>
            </Table.Th>
            <Table.Th>
              <SectionLabel>Agente responsável</SectionLabel>
            </Table.Th>
            <Table.Th>
              <SectionLabel>Estado</SectionLabel>
            </Table.Th>
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
    </SectionedCard>
  );
}
