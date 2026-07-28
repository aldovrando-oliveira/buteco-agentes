import { Card, Stack, Text, Title } from '@mantine/core';
import type { Agent } from '../types/agent';

interface AgentDetailCardProps {
  agent: Agent;
}

export function AgentDetailCard({ agent }: AgentDetailCardProps) {
  return (
    <Card withBorder>
      <Stack gap="sm">
        <Title order={2}>{agent.name}</Title>
        <Text>{agent.instructions}</Text>
        <Text size="sm" c="dimmed">
          Criado em {new Date(agent.createdAt).toLocaleString('pt-BR')}
        </Text>
        <Text size="sm" c="dimmed">
          Atualizado em {new Date(agent.updatedAt).toLocaleString('pt-BR')}
        </Text>
      </Stack>
    </Card>
  );
}
