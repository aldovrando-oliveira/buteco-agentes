import { Badge, Card, Group, ScrollArea, Stack, Text, Title, Typography } from '@mantine/core';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import type { Agent } from '../types/agent';

interface AgentDetailCardProps {
  agent: Agent;
}

export function AgentDetailCard({ agent }: AgentDetailCardProps) {
  return (
    <Card withBorder>
      <Stack gap="sm">
        <Group justify="space-between">
          <Title order={2}>{agent.name}</Title>
          <Badge color={agent.isActive ? 'green' : 'gray'}>
            {agent.isActive ? 'Ativo' : 'Inativo'}
          </Badge>
        </Group>
        <ScrollArea h="calc(100vh - 320px)" mih={220} data-testid="instructions-scroll-area">
          <Typography>
            <ReactMarkdown remarkPlugins={[remarkGfm]}>{agent.instructions}</ReactMarkdown>
          </Typography>
        </ScrollArea>
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
