import { ActionIcon, Button, Group, Stack, Text, TextInput } from '@mantine/core';
import type { UseFormReturnType } from '@mantine/form';
import type { AgentFormValues } from './agentFormValues';

interface AgentSkillsFieldsProps {
  // O componente só opera sobre `form.values.skills` — não tem estado próprio.
  form: UseFormReturnType<AgentFormValues>;
  // Erros vindos do servidor já indexados por linha (Decision 3 do
  // design.md): AgentForm converte `skills[i].name` para `nameErrors[i]`.
  nameErrors?: Record<number, string>;
}

function RemoveIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M18 6 6 18M6 6l12 12" />
    </svg>
  );
}

export function AgentSkillsFields({ form, nameErrors }: AgentSkillsFieldsProps) {
  const skills = form.values.skills;

  return (
    <Stack gap="xs" data-testid="agent-skills-fields">
      <div>
        <Text size="sm" fw={500}>
          Skills
        </Text>
        <Text size="xs" c="dimmed">
          Rótulos do que o agente sabe fazer. Não afetam o runtime.
        </Text>
      </div>

      {skills.map((_, index) => (
        <Group
          key={form.key(`skills.${index}`)}
          align="flex-start"
          gap="xs"
          wrap="nowrap"
          data-testid={`agent-skill-row-${index}`}
        >
          <TextInput
            label="Nome da skill"
            placeholder="ex.: Segunda via de boleto"
            withAsterisk
            style={{ flex: 1 }}
            {...form.getInputProps(`skills.${index}.name`)}
            error={nameErrors?.[index] ?? form.getInputProps(`skills.${index}.name`).error}
          />
          <TextInput
            label="Descrição da skill"
            placeholder="Opcional"
            style={{ flex: 2 }}
            {...form.getInputProps(`skills.${index}.description`)}
          />
          <ActionIcon
            variant="default"
            mt={26}
            aria-label={`Remover skill ${index + 1}`}
            onClick={() => form.removeListItem('skills', index)}
          >
            <RemoveIcon />
          </ActionIcon>
        </Group>
      ))}

      <Group>
        <Button
          type="button"
          variant="default"
          size="xs"
          onClick={() => form.insertListItem('skills', { name: '', description: '' })}
        >
          Adicionar skill
        </Button>
      </Group>
    </Stack>
  );
}
