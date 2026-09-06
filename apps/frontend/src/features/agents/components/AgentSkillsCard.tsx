import { Badge, Stack, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import type { AgentSkill } from '../types/agent';

interface AgentSkillsCardProps {
  skills: AgentSkill[];
}

// Presentational: nasce como card separado porque a change seguinte
// (detalhe em abas) o reposiciona na coluna direita da Visão geral
// (Decision 4 do design.md da change frontend-agente-description-skills).
export function AgentSkillsCard({ skills }: AgentSkillsCardProps) {
  return (
    <SectionedCard data-testid="agent-skills-card" title="Skills">
      <SectionedCard.Body>
        {skills.length === 0 ? (
          <Text size="sm" c="dimmed">
            Nenhuma skill declarada.
          </Text>
        ) : (
          <Stack gap="xs" component="ul" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {skills.map((skill) => (
              <li key={skill.name}>
                <Badge variant="light" color="gray" radius="sm" tt="none" fw={500}>
                  {skill.name}
                </Badge>
                {skill.description && (
                  <Text size="sm" c="dimmed" mt={2}>
                    {skill.description}
                  </Text>
                )}
              </li>
            ))}
          </Stack>
        )}
      </SectionedCard.Body>
    </SectionedCard>
  );
}
