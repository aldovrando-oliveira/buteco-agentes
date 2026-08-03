import { Badge, Card, Group, Stack, Text, Title } from '@mantine/core';
import type { McpServer } from '../types/mcpServer';

interface McpServerDetailCardProps {
  mcpServer: McpServer;
}

const authTypeLabels: Record<McpServer['authType'], string> = {
  None: 'Nenhuma',
  BearerToken: 'Bearer Token',
};

export function McpServerDetailCard({ mcpServer }: McpServerDetailCardProps) {
  return (
    <Card withBorder>
      <Stack gap="sm">
        <Group justify="space-between">
          <Title order={2}>{mcpServer.name}</Title>
          <Badge color={mcpServer.isActive ? 'green' : 'gray'}>
            {mcpServer.isActive ? 'Ativo' : 'Inativo'}
          </Badge>
        </Group>
        <Text size="sm" c="dimmed">
          Url: {mcpServer.url} · Autenticação: {authTypeLabels[mcpServer.authType]}
        </Text>
        {mcpServer.description && <Text>{mcpServer.description}</Text>}
        <Text size="sm" c="dimmed">
          Criado em {new Date(mcpServer.createdAt).toLocaleString('pt-BR')}
        </Text>
        <Text size="sm" c="dimmed">
          Atualizado em {new Date(mcpServer.updatedAt).toLocaleString('pt-BR')}
        </Text>
      </Stack>
    </Card>
  );
}
