import { Alert, Badge, Grid, Group, ScrollArea, Stack, Text, Typography } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { AgentSkillsCard } from './AgentSkillsCard';
import type { Agent } from '../types/agent';

interface AgentOverviewTabProps {
  agent: Agent;
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
        <SectionedCard
          title="Instruções (system prompt)"
          // O protótipo marca no cabeçalho que o campo aceita Markdown, que é
          // informação que o operador só descobriria escrevendo.
          action={
            <Badge variant="default" radius="sm" fw={500} ff="monospace">
              markdown
            </Badge>
          }
        >
          <SectionedCard.Body>
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
          </SectionedCard.Body>
        </SectionedCard>
      </Grid.Col>

      <Grid.Col span={{ base: 12, md: 5 }}>
        <Stack gap="md">
          <SectionedCard title="Modelo">
            <Stack gap="sm" px="md" py="sm">
              <DefinitionRow label="Provedor" value={agent.provider ?? '—'} />
              <DefinitionRow label="Modelo" value={agent.model ?? 'não configurado'} />
              {needsReconfiguration && (
                <Alert color="yellow" data-testid="reconfiguration-callout">
                  Este agente foi criado antes do multi-provedor. Escolha provedor e modelo em
                  Editar.
                </Alert>
              )}
            </Stack>
          </SectionedCard>

          <AgentSkillsCard skills={agent.skills} />

          <SectionedCard title="Datas">
            <Stack gap="sm" px="md" py="sm">
              <DefinitionRow
                label="Criado em"
                value={new Date(agent.createdAt).toLocaleString('pt-BR')}
              />
              <DefinitionRow
                label="Atualizado em"
                value={new Date(agent.updatedAt).toLocaleString('pt-BR')}
              />
            </Stack>
          </SectionedCard>
        </Stack>
      </Grid.Col>
    </Grid>
  );
}
