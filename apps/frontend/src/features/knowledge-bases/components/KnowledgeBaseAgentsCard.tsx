import { Anchor, Badge, Group, Stack, Text } from '@mantine/core';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { agentsConsultingBase } from '../utils/agentUsage';
import type { Agent } from '../../agents/types/agent';

interface KnowledgeBaseAgentsCardProps {
  knowledgeBaseId: string;
  // Catálogo de agentes buscado pela página e repassado como propriedade
  // (convenção 7). Indefinido quando a consulta falha — nesse caso o card não
  // afirma nada sobre uso, e o resto do detalhe continua servindo, mesmo
  // tratamento de McpServerAgentsCard.
  agents?: Agent[];
}

export function KnowledgeBaseAgentsCard({ knowledgeBaseId, agents }: KnowledgeBaseAgentsCardProps) {
  if (!agents) {
    return (
      <SectionedCard title="Agentes que consultam esta base">
        <SectionedCard.Body>
          <Text size="sm" c="dimmed" data-testid="agents-unavailable">
            Não foi possível carregar os agentes, então não dá para dizer quem consulta esta base.
          </Text>
        </SectionedCard.Body>
      </SectionedCard>
    );
  }

  const consulting = agentsConsultingBase(agents, knowledgeBaseId);

  return (
    <SectionedCard title="Agentes que consultam esta base">
      {consulting.length === 0 ? (
        <SectionedCard.Body>
          {/* A tela de vínculo passou a existir (change
              frontend-agente-aba-conhecimento), então a copy deixa de anunciar
              etapa futura e diz onde vincular. O protótipo mandava "Vincule-a na
              Visão geral de um agente", e isso continua errado: a revisão 2 do
              próprio handoff moveu o vínculo da Visão geral para uma aba
              própria. Sem link para um agente específico — não há qual escolher
              a partir daqui. */}
          <Text size="sm" c="dimmed" ta="center" py="lg" data-testid="agents-empty">
            Nenhum agente consulta esta base. O vínculo é feito na aba Conhecimento do detalhe do
            agente.
          </Text>
        </SectionedCard.Body>
      ) : (
        <Stack gap={0}>
          {consulting.map((agent) => (
            <SectionedCard.Row key={agent.id}>
              <Group justify="space-between" wrap="nowrap" gap="sm">
                <Anchor component={Link} to={`/agents/${agent.id}`} size="sm" fw={600}>
                  {agent.name}
                </Anchor>
                <Badge color={agent.isActive ? 'green' : 'gray'}>
                  {agent.isActive ? 'Ativo' : 'Inativo'}
                </Badge>
              </Group>
            </SectionedCard.Row>
          ))}
        </Stack>
      )}
    </SectionedCard>
  );
}
