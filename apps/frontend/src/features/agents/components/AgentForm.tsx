import {
  Button,
  Group,
  Paper,
  Select,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core';
import { useForm } from '@mantine/form';
import type { AgentSkill, CreateAgentInput, ProviderCatalogEntry } from '../types/agent';
import { AgentSkillsFields } from './AgentSkillsFields';
import type { AgentFormValues, AgentSkillFormValue } from './agentFormValues';

interface AgentFormProps {
  onSubmit: (values: CreateAgentInput) => void;
  onCancel: () => void;
  providers: ProviderCatalogEntry[];
  errors?: Record<string, string>;
  submitting?: boolean;
  initialValues?: CreateAgentInput;
  submitLabel?: string;
}

interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

const SKILL_NAME_REQUIRED = 'O nome da skill é obrigatório.';

// Injeta uma opção sintética desabilitada quando o valor atual não consta
// nas opções disponíveis (provider/model removido do ambiente depois do
// cadastro do agente) — informativo, mas não re-selecionável. Ver Decision
// 2 do design.md da change frontend-selecao-provider-modelo.
function withStaleOption(options: SelectOption[], currentValue: string): SelectOption[] {
  if (currentValue && !options.some((option) => option.value === currentValue)) {
    return [
      ...options,
      { value: currentValue, label: `${currentValue} (indisponível)`, disabled: true },
    ];
  }
  return options;
}

function toFormValues(input?: CreateAgentInput): AgentFormValues {
  return {
    name: input?.name ?? '',
    description: input?.description ?? '',
    instructions: input?.instructions ?? '',
    provider: input?.provider ?? '',
    model: input?.model ?? '',
    skills: (input?.skills ?? []).map((skill) => ({
      name: skill.name,
      description: skill.description ?? '',
    })),
  };
}

function toSkills(values: AgentSkillFormValue[]): AgentSkill[] {
  return values.map((skill) => ({
    name: skill.name.trim(),
    description: skill.description.trim() || null,
  }));
}

// Erros de validação do servidor chegam como `skills[0].name` (formato do
// ValidateShape da API); aqui viram um mapa por índice de linha.
function skillNameErrorsFrom(errors?: Record<string, string>): Record<number, string> {
  const result: Record<number, string> = {};
  for (const [key, message] of Object.entries(errors ?? {})) {
    const match = /^skills\[(\d+)\]\.name$/i.exec(key);
    if (match) {
      result[Number(match[1])] = message;
    }
  }
  return result;
}

// Superfície própria: o fundo da página deixou de ser branco (change
// frontend-tema-identidade-visual, D4/D12), então o conteúdo precisa declarar
// a sua em vez de herdar o branco do body.
export function AgentForm({
  onSubmit,
  onCancel,
  providers,
  errors,
  submitting,
  initialValues,
  submitLabel = 'Criar agente',
}: AgentFormProps) {
  const form = useForm<AgentFormValues>({
    initialValues: toFormValues(initialValues),
    validate: {
      name: (value) => (value.trim() ? null : 'O nome do agente é obrigatório.'),
      instructions: (value) =>
        value.trim() ? null : 'As instruções (system prompt) do agente são obrigatórias.',
      provider: (value) => (value.trim() ? null : 'O provedor de LLM do agente é obrigatório.'),
      model: (value) => (value.trim() ? null : 'O modelo do agente é obrigatório.'),
      skills: {
        name: (value: string) => (value.trim() ? null : SKILL_NAME_REQUIRED),
      },
    },
  });

  const providerOptions = withStaleOption(
    providers.map((provider) => ({ value: provider.id, label: provider.id })),
    form.values.provider,
  );

  const selectedProvider = providers.find((provider) => provider.id === form.values.provider);
  const modelOptions = withStaleOption(
    (selectedProvider?.models ?? []).map((model) => ({ value: model, label: model })),
    form.values.model,
  );

  const handleSubmit = (values: AgentFormValues) => {
    onSubmit({
      name: values.name,
      instructions: values.instructions,
      provider: values.provider,
      model: values.model,
      description: values.description.trim() || null,
      skills: toSkills(values.skills),
    });
  };

  return (
    <form onSubmit={form.onSubmit(handleSubmit)}>
      <Paper withBorder radius="md" p="lg">
        <Stack>
          <TextInput
            label="Nome"
            placeholder="Nome do agente"
            withAsterisk
            {...form.getInputProps('name')}
            error={errors?.name ?? form.errors.name}
          />
          <Textarea
            label="Descrição"
            placeholder="Descrição do agente"
            description="Uso interno: ajuda o operador a identificar o agente nas listas."
            rows={2}
            resize="vertical"
            {...form.getInputProps('description')}
            error={errors?.description ?? form.errors.description}
          />
          <Textarea
            label="Instruções"
            placeholder="System prompt do agente"
            description="System prompt, aceita Markdown."
            withAsterisk
            rows={12}
            resize="vertical"
            styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)' } }}
            {...form.getInputProps('instructions')}
            error={errors?.instructions ?? form.errors.instructions}
          />
          <Text size="xs" c="dimmed" mt={-8} data-testid="instructions-counter">
            {form.values.instructions.length} caracteres
          </Text>
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <Select
              label="Provedor"
              placeholder="Selecione o provedor de LLM"
              withAsterisk
              data={providerOptions}
              value={form.values.provider || null}
              onChange={(value) => {
                form.setFieldValue('provider', value ?? '');
                form.setFieldValue('model', '');
              }}
              error={errors?.provider ?? form.errors.provider}
            />
            <Select
              label="Modelo"
              placeholder={
                form.values.provider ? 'Selecione o modelo' : 'Escolha o provedor primeiro'
              }
              withAsterisk
              disabled={!form.values.provider}
              data={modelOptions}
              value={form.values.model || null}
              onChange={(value) => form.setFieldValue('model', value ?? '')}
              error={errors?.model ?? form.errors.model}
            />
          </SimpleGrid>
          <AgentSkillsFields form={form} nameErrors={skillNameErrorsFrom(errors)} />
          <Group>
            <Button type="submit" loading={submitting}>
              {submitLabel}
            </Button>
            <Button type="button" variant="default" onClick={onCancel}>
              Cancelar
            </Button>
          </Group>
        </Stack>
      </Paper>
    </form>
  );
}
