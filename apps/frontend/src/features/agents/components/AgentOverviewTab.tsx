import { Alert, Card, Grid, Group, ScrollArea, Stack, Text, Typography } from '@mantine/core';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { AgentSkillsCard } from './AgentSkillsCard';
import type { Agent } from '../types/agent';

interface AgentOverviewTabProps {
  agent: Agent;
}

function CardHeading({ children }: { children: string }) {
  return (
    <Text size="xs" fw={600} tt="uppercase" c="dimmed">
      {children}
    </Text>
  );
}

function DefinitionRow({ label, value }: { label: string; value: string }) {
  return (
    <Group justify="space-between" gap="sm" wrap="nowrap">
      <Text size="sm" c="dimmed">
        {label}
      </Text>
      <Text size="sm" ff="monospace" ta="right">
        {value}
      </Text>
    </Group>
  );
}

// Instruções de um lado, cartões de contexto do outro. Substitui o card
// único que reunia tudo: nome, estado e descrição subiram para o cabeçalho
// da página, e o resumo textual de servidores MCP saiu porque a aba de
// ferramentas mostra a mesma informação com mais precisão (Decision 7 do
// design.md da change frontend-agente-detalhe-abas).
export function AgentOverviewTab({ agent }: AgentOverviewTabProps) {
  const needsReconfiguration = agent.provider === null || agent.model === null;

  return (
    <Grid gap="md" align="start">
      <Grid.Col span={{ base: 12, md: 7 }}>
        <Card withBorder>
          <Stack gap="sm">
            <CardHeading>Instruções (system prompt)</CardHeading>
            {/* `height` (não `max-height`) é obrigatório aqui: o Viewport
                interno do ScrollArea do Mantine é estilizado com
                `height: 100%`, que só resolve contra uma altura explícita
                do container pai. Com altura automática o Viewport cresce
                para caber todo o conteúdo e a rolagem fica inerte. */}
            <ScrollArea h="calc(100vh - 360px)" mih={220} data-testid="instructions-scroll-area">
              <Typography>
                <ReactMarkdown remarkPlugins={[remarkGfm]}>{agent.instructions}</ReactMarkdown>
              </Typography>
            </ScrollArea>
          </Stack>
        </Card>
      </Grid.Col>

      <Grid.Col span={{ base: 12, md: 5 }}>
        <Stack gap="md">
          <Card withBorder>
            <Stack gap="sm">
              <CardHeading>Modelo</CardHeading>
              <DefinitionRow label="Provedor" value={agent.provider ?? '—'} />
              <DefinitionRow label="Modelo" value={agent.model ?? 'não configurado'} />
              {needsReconfiguration && (
                <Alert color="yellow" data-testid="reconfiguration-callout">
                  Este agente foi criado antes do multi-provedor. Escolha provedor e modelo em
                  Editar.
                </Alert>
              )}
            </Stack>
          </Card>

          <AgentSkillsCard skills={agent.skills} />

          <Card withBorder>
            <Stack gap="sm">
              <CardHeading>Datas</CardHeading>
              <DefinitionRow
                label="Criado em"
                value={new Date(agent.createdAt).toLocaleString('pt-BR')}
              />
              <DefinitionRow
                label="Atualizado em"
                value={new Date(agent.updatedAt).toLocaleString('pt-BR')}
              />
            </Stack>
          </Card>
        </Stack>
      </Grid.Col>
    </Grid>
  );
}
