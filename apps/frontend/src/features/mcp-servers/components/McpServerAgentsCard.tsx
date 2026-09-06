import { Anchor, Badge, Group, Text } from '@mantine/core';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
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
      <SectionedCard data-testid="mcp-server-agents-card" title="Agentes que usam este servidor">
        <SectionedCard.Row>
          <Text size="sm" c="dimmed">
            Não foi possível carregar a informação de uso deste servidor.
          </Text>
        </SectionedCard.Row>
      </SectionedCard>
    );
  }

  const usages = agentsUsingServer(agents, mcpServerId);

  return (
    <SectionedCard
      data-testid="mcp-server-agents-card"
      title="Agentes que usam este servidor"
      // A contagem fica ao lado do rótulo, em peso normal, e não embutida
      // nele: o rótulo nomeia a seção, a contagem informa o estado dela.
      action={
        usages.length > 0 && (
          <Text size="xs" c="dimmed">
            {usages.length} {usages.length === 1 ? 'agente usa' : 'agentes usam'} este servidor MCP
          </Text>
        )
      }
    >
      {usages.length === 0 ? (
        <SectionedCard.Row>
          <Text size="sm" c="dimmed">
            Nenhum agente usa este servidor. Desativá-lo não afeta nenhum agente agora.
          </Text>
        </SectionedCard.Row>
      ) : (
        usages.map(({ agent, allowedTools }) => (
          <SectionedCard.Row key={agent.id}>
            <Group
              justify="space-between"
              align="center"
              wrap="nowrap"
              gap="md"
              data-testid={`server-agent-row-${agent.id}`}
            >
              <Anchor component={Link} to={`/agents/${agent.id}`} size="sm" fw={600}>
                {agent.name}
              </Anchor>

              {/* As tools seguem o nome, alinhadas à esquerda: são atributo do
                  vínculo, não uma coluna à direita. */}
              <Group gap={6} justify="flex-start" style={{ flex: 1, minWidth: 0 }}>
                {allowedTools.length === 0 ? (
                  <Badge color="yellow">Vinculado sem tools</Badge>
                ) : (
                  allowedTools.map((tool) => (
                    <Badge
                      key={tool}
                      variant="default"
                      radius="sm"
                      fw={500}
                      style={{ fontFamily: 'var(--mantine-font-family-monospace)' }}
                    >
                      {tool}
                    </Badge>
                  ))
                )}
              </Group>

              <Badge color={agent.isActive ? 'green' : 'gray'} style={{ flexShrink: 0 }}>
                {agent.isActive ? 'Ativo' : 'Inativo'}
              </Badge>
            </Group>
          </SectionedCard.Row>
        ))
      )}
    </SectionedCard>
  );
}
