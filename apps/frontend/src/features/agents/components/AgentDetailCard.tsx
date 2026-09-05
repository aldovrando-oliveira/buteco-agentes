import { Badge, Card, Group, ScrollArea, Stack, Text, Title, Typography } from '@mantine/core';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import type { Agent } from '../types/agent';

interface AgentDetailCardProps {
  agent: Agent;
}

export function AgentDetailCard({ agent }: AgentDetailCardProps) {
  const needsReconfiguration = agent.provider === null || agent.model === null;

  return (
    <Card withBorder>
      <Stack gap="sm">
        <Group justify="space-between">
          <Title order={2}>{agent.name}</Title>
          <Group gap="xs">
            {needsReconfiguration && <Badge color="yellow">Precisa de reconfiguração</Badge>}
            <Badge color={agent.isActive ? 'green' : 'gray'}>
              {agent.isActive ? 'Ativo' : 'Inativo'}
            </Badge>
          </Group>
        </Group>
        <Text size="sm" c={agent.description ? undefined : 'dimmed'}>
          {agent.description ?? 'Sem descrição.'}
        </Text>
        <Text size="sm" c="dimmed">
          Provider: {agent.provider ?? '—'} · Model: {agent.model ?? '—'}
        </Text>
        <Text size="sm" c="dimmed">
          Servidores MCP vinculados:{' '}
          {agent.mcpServers.length > 0
            ? agent.mcpServers.map((mcpServer) => mcpServer.name).join(', ')
            : 'nenhum servidor MCP vinculado'}
        </Text>
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
