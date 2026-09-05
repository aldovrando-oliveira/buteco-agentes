import { Anchor, Badge, Card, Group, Stack, Text } from '@mantine/core';
import { Link } from 'react-router';
import { agentsUsingServer } from '../utils/agentUsage';
import type { Agent } from '../../agents/types/agent';

interface McpServerAgentsCardProps {
  mcpServerId: string;
  agents?: Agent[];
}

// Visão inversa: a partir do servidor, quem o usa. É leitura — o vínculo
// continua sendo editado a partir do agente.
export function McpServerAgentsCard({ mcpServerId, agents }: McpServerAgentsCardProps) {
  if (!agents) {
    return (
      <Card withBorder data-testid="mcp-server-agents-card">
        <Text size="sm" c="dimmed">
          Não foi possível carregar a informação de uso deste servidor.
        </Text>
      </Card>
    );
  }

  const usages = agentsUsingServer(agents, mcpServerId);

  return (
    <Card withBorder data-testid="mcp-server-agents-card">
      <Stack gap="sm">
        <Text size="xs" fw={600} tt="uppercase" c="dimmed">
          {usages.length === 0
            ? 'Agentes que usam este servidor'
            : `${usages.length} ${usages.length === 1 ? 'agente usa' : 'agentes usam'} este servidor`}
        </Text>

        {usages.length === 0 ? (
          <Text size="sm" c="dimmed">
            Nenhum agente usa este servidor. Desativá-lo não afeta nenhum agente agora.
          </Text>
        ) : (
          <Stack gap="xs">
            {usages.map(({ agent, allowedTools }) => (
              <Group
                key={agent.id}
                justify="space-between"
                align="flex-start"
                wrap="nowrap"
                data-testid={`server-agent-row-${agent.id}`}
              >
                <Anchor component={Link} to={`/agents/${agent.id}`} size="sm">
                  {agent.name}
                </Anchor>

                <Group gap={4} justify="flex-end" style={{ flex: 1, minWidth: 0 }}>
                  {allowedTools.length === 0 ? (
                    <Badge color="yellow">Vinculado sem tools</Badge>
                  ) : (
                    allowedTools.map((tool) => (
                      <Badge key={tool} variant="default" tt="none" fw={500}>
                        {tool}
                      </Badge>
                    ))
                  )}
                </Group>

                <Badge color={agent.isActive ? 'green' : 'gray'}>
                  {agent.isActive ? 'Ativo' : 'Inativo'}
                </Badge>
              </Group>
            ))}
          </Stack>
        )}
      </Stack>
    </Card>
  );
}
