import { Button, Stack, Textarea, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import type { CreateAgentInput } from '../types/agent';

interface AgentFormProps {
  onSubmit: (values: CreateAgentInput) => void;
  errors?: Record<string, string>;
  submitting?: boolean;
  initialValues?: CreateAgentInput;
  submitLabel?: string;
}

export function AgentForm({
  onSubmit,
  errors,
  submitting,
  initialValues,
  submitLabel = 'Criar agente',
}: AgentFormProps) {
  const form = useForm<CreateAgentInput>({
    initialValues: initialValues ?? { name: '', instructions: '' },
    validate: {
      name: (value) => (value.trim() ? null : 'O nome do agente é obrigatório.'),
      instructions: (value) =>
        value.trim() ? null : 'As instruções (system prompt) do agente são obrigatórias.',
    },
  });

  return (
    <form onSubmit={form.onSubmit((values) => onSubmit(values))}>
      <Stack>
        <TextInput
          label="Nome"
          placeholder="Nome do agente"
          withAsterisk
          {...form.getInputProps('name')}
          error={errors?.name ?? form.errors.name}
        />
        <Textarea
          label="Instruções"
          placeholder="System prompt do agente"
          withAsterisk
          rows={12}
          resize="vertical"
          {...form.getInputProps('instructions')}
          error={errors?.instructions ?? form.errors.instructions}
        />
        <Button type="submit" loading={submitting}>
          {submitLabel}
        </Button>
      </Stack>
    </form>
  );
}
